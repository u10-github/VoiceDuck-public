using System.Linq;
using System.Globalization;

namespace VoiceDuck.Core;

public class VolumeDuckingService
{
    private readonly IAudioSessionVolumeWriter _volumeWriter;
    private readonly DuckingSessionClassifier _classifier;
    private readonly ApplicationVolumeStateStore _stateStore;
    private readonly IRestorationObligationRepository _obligationRepo;
    private readonly IAudioEndpointSelector _endpointSelector;
    private readonly ILogger? _logger;
    private HashSet<ApplicationAudioIdentity> _lastDeferredNotFound = new();
    private HashSet<ApplicationAudioIdentity> _lastDuckResult = new();
    private HashSet<ApplicationAudioIdentity> _lastAlreadyPending = new();
    private readonly Dictionary<ApplicationAudioIdentity, string> _lastBaselineDecisions = new();
    private Dictionary<EligibilityDiagnosticKey, ControlEligibilityRejectionReason> _lastEligibilityRejections = new();
    private bool _lastBaselineDecisionWasNoCandidates;
    private IReadOnlyList<RestorationObligation> _lastObservedObligations =
        Array.Empty<RestorationObligation>();

    private EndpointDiag _lastEpState = new EndpointDiag.Unobserved();

    private abstract record EndpointDiag
    {
        private EndpointDiag() { }

        public sealed record Unobserved : EndpointDiag;
        public sealed record LookupFailure(string Kind, string Content) : EndpointDiag;
        public sealed record Selected(string EndpointId, HashSet<string> RejectedSet) : EndpointDiag;
    }

    public VolumeDuckingService(
        IAudioSessionVolumeWriter volumeWriter,
        DuckingSessionClassifier classifier,
        ApplicationVolumeStateStore stateStore,
        IRestorationObligationRepository obligationRepo,
        IAudioEndpointSelector endpointSelector,
        ILogger? logger = null)
    {
        _volumeWriter = volumeWriter ?? throw new ArgumentNullException(nameof(volumeWriter));
        _classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _obligationRepo = obligationRepo ?? throw new ArgumentNullException(nameof(obligationRepo));
        _endpointSelector = endpointSelector ?? throw new ArgumentNullException(nameof(endpointSelector));
        _logger = logger;
    }

    public DuckingStateSnapshot CaptureStateSnapshot(
        DuckingPhase phase,
        IEnumerable<string> activeTriggers)
    {
        ArgumentNullException.ThrowIfNull(activeTriggers);

        var selectedEndpointId = _lastEpState is EndpointDiag.Selected selected
            ? selected.EndpointId
            : null;
        var obligationsByIdentity = _lastObservedObligations
            .GroupBy(obligation => obligation.Identity)
            .ToDictionary(
                group => group.Key,
                group => group.Select(obligation => obligation.Status).ToArray());
        var applications = _stateStore.GetAll()
            .Select(state => new DuckingApplicationStateSnapshot(
                state.Identity,
                state.BaselineVolume,
                state.IsDucked,
                obligationsByIdentity.TryGetValue(state.Identity, out var statuses)
                    ? statuses
                    : Array.Empty<RestorationStatus>()))
            .ToArray();

        return new DuckingStateSnapshot(
            phase,
            selectedEndpointId,
            activeTriggers,
            applications);
    }

    private RestorationObligationLoadResult LoadObligations()
    {
        var result = _obligationRepo.LoadAll();
        _lastObservedObligations = Array.AsReadOnly(result.Obligations.ToArray());
        return result;
    }

    private void SaveObligations(IReadOnlyList<RestorationObligation> obligations)
    {
        _obligationRepo.SaveAll(obligations);
        _lastObservedObligations = Array.AsReadOnly(obligations.ToArray());
    }

    public StartupRecoveryResult LoadAndPopulateStartupState()
    {
        var result = LoadObligations();
        var now = DateTimeOffset.UtcNow;
        var obligations = result.Obligations
            .Where(o => o.Identity.IsResolved)
            .ToList();
        var duplicateObligations = FindDuplicateObligations(obligations);
        if (duplicateObligations.Count > 0)
        {
            foreach (var duplicate in duplicateObligations)
                LogDuplicateObligationDecision(duplicate.Identity, duplicate.Baselines);
            return new StartupRecoveryResult(result.WasCorrupt, Saved: false, LoadedCount: 0);
        }

        for (var i = 0; i < obligations.Count; i++)
        {
            obligations[i] = new RestorationObligation(
                obligations[i].Identity,
                obligations[i].BaselineVolume,
                RestorationStatus.RestorePending,
                obligations[i].CreatedAt,
                now,
                obligations[i].SchemaVersion);
        }

        var saved = false;
        if (result.WasCorrupt || obligations.Count > 0)
        {
            try
            {
                SaveObligations(obligations);
                saved = true;
            }
            catch (Exception ex)
            {
                _logger?.Error($"StartupSave: {ex.Message}");
            }
        }

        var loaded = 0;
        foreach (var obl in obligations)
        {
            if (_stateStore.TryGet(obl.Identity, out _))
                continue;

            _stateStore.Add(new ApplicationVolumeState(
                obl.Identity, obl.BaselineVolume, isDucked: true));
            loaded++;
            _logger?.Info($"StartupLoad: {obl.Identity} baseline={obl.BaselineVolume:F2} persist={(saved ? "saved" : "failed")}");
        }

        return new StartupRecoveryResult(result.WasCorrupt, saved, loaded);
    }

    public sealed record StartupRecoveryResult(bool WasCorrupt, bool Saved, int LoadedCount);

    private string? ResolveRelevantEndpoint(IReadOnlyList<AudioSessionInfo> sessions)
    {
        string? endpointId;
        string? failureKind = null;
        string? failureContent = null;

        try
        {
            endpointId = _endpointSelector.GetDefaultMultimediaEndpointId();
        }
        catch (Exception ex)
        {
            endpointId = null;
            failureKind = "exception";
            failureContent = ex.Message;
        }

        if (string.IsNullOrWhiteSpace(endpointId))
        {
            var currentFailureKind = failureKind ?? "empty";
            var currentFailureContent = failureContent ?? "";

            var failureChanged = _lastEpState switch
            {
                EndpointDiag.Unobserved => true,
                EndpointDiag.LookupFailure(var k, var c) => k != currentFailureKind || c != currentFailureContent,
                _ => true
            };

            if (failureChanged)
            {
                if (failureKind == "exception")
                    _logger?.Error($"EndpointSelection: endpoint=(null) selected=false reason=lookup_failed error={currentFailureContent}");
                else
                    _logger?.Warn("EndpointSelection: endpoint=(null) selected=false reason=lookup_failed");
                _lastEpState = new EndpointDiag.LookupFailure(currentFailureKind, currentFailureContent);
            }
            return null;
        }

        var rejectedSet = sessions
            .Select(s => s.Identity.RenderDeviceId)
            .Where(ep => !string.Equals(ep, endpointId, StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

        var changed = _lastEpState switch
        {
            EndpointDiag.Selected(var id, var set) => id != endpointId || !RejectedSetsEqual(set, rejectedSet),
            _ => true
        };

        if (changed)
        {
            _logger?.Info($"EndpointSelection: endpoint={endpointId} selected=true reason=default_multimedia");
            foreach (var ep in rejectedSet)
                _logger?.Info($"EndpointSelection: endpoint={ep} selected=false reason=not_default_multimedia");
            _lastEpState = new EndpointDiag.Selected(endpointId, rejectedSet);
        }

        return endpointId;
    }

    private IReadOnlyList<AudioSessionInfo> ApplyEndpointFilter(IReadOnlyList<AudioSessionInfo> sessions)
    {
        var endpointId = ResolveRelevantEndpoint(sessions);
        if (endpointId is null)
            return Array.Empty<AudioSessionInfo>();

        return sessions
            .Where(session => string.Equals(
                session.Identity.RenderDeviceId,
                endpointId,
                StringComparison.Ordinal))
            .ToList();
    }

    private static bool RejectedSetsEqual(HashSet<string>? a, HashSet<string>? b)
    {
        if (a is null)
            return b is null || b.Count == 0;
        if (b is null)
            return a.Count == 0;
        return a.SetEquals(b);
    }

    public void ApplyDucking(
        IReadOnlyList<AudioSessionInfo> sessions,
        VoiceDuckSettings settings,
        string voiceDuckProcessName)
    {
        var relevantEndpointId = ResolveRelevantEndpoint(sessions);
        var currentRejections =
            new Dictionary<EligibilityDiagnosticKey, ControlEligibilityRejectionReason>();
        var groups = ApplicationAudioSessionGroup.GroupSessions(
            sessions,
            voiceDuckProcessName,
            _classifier,
            settings,
            relevantEndpointId,
            (session, rejected) =>
            {
                currentRejections[EligibilityDiagnosticKey.From(session)] = rejected.Reason;
            });
        LogEligibilityRejections(currentRejections);

        if (groups.Count == 0)
        {
            _lastBaselineDecisions.Clear();
            if (!_lastBaselineDecisionWasNoCandidates)
            {
                _logger?.Info(
                    "BaselineDecision: identity=(none) candidates=[] spread=0 outcome=rejected reason=no_candidates baseline=none endpoint_relevance=none relevance_reason=no_eligible_session_after_filter");
                _lastBaselineDecisionWasNoCandidates = true;
            }
            return;
        }

        _lastBaselineDecisionWasNoCandidates = false;
        var currentIdentities = groups.Select(group => group.Identity).ToHashSet();
        var now = DateTimeOffset.UtcNow;
        var newObligations = new List<RestorationObligation>();
        var newlyTracked = new List<ApplicationAudioIdentity>();
        var existingStateSnapshots =
            new Dictionary<ApplicationAudioIdentity, ApplicationVolumeState>();

        var loadResult = LoadObligations();
        if (loadResult.WasCorrupt)
        {
            PruneBaselineDecisions(currentIdentities);
            return;
        }

        var existingObligations = loadResult.Obligations;
        var duplicateObligations = FindDuplicateObligations(existingObligations);
        var duplicateIdentities = duplicateObligations
            .Select(duplicate => duplicate.Identity)
            .ToHashSet();
        if (duplicateObligations.Count > 0)
        {
            var evaluatedIdentities = currentIdentities
                .Concat(duplicateIdentities)
                .ToHashSet();
            PruneBaselineDecisions(evaluatedIdentities);
            foreach (var duplicate in duplicateObligations)
            {
                LogDuplicateObligationDecision(
                    duplicate.Identity,
                    duplicate.Baselines);
            }
        }
        else
        {
            PruneBaselineDecisions(currentIdentities);
        }
        var acceptedGroups = new List<ApplicationAudioSessionGroup>();

        foreach (var group in groups)
        {
            if (duplicateIdentities.Contains(group.Identity))
                continue;

            var wasExisting = _stateStore.TryGet(group.Identity, out var existing) && existing is not null;
            var existingObl = FindObligation(existingObligations, group.Identity);

            if (wasExisting)
            {
                existingStateSnapshots[group.Identity] = new ApplicationVolumeState(
                    existing!.Identity,
                    existing.BaselineVolume,
                    existing.IsDucked);
            }

            if (wasExisting && existingObl is not null)
            {
                var reason = existing!.BaselineVolume == existingObl.BaselineVolume
                    ? "existing_obligation"
                    : "reconciled_durable_obligation";
                if (reason == "reconciled_durable_obligation")
                {
                    existing = new ApplicationVolumeState(
                        group.Identity, existingObl.BaselineVolume, existing.IsDucked);
                    _stateStore.Add(existing);
                }

                LogBaselineDecision(
                    group, "selected", reason, existingObl.BaselineVolume);
            }
            else if (!wasExisting)
            {
                float baseline;
                if (existingObl is not null)
                {
                    baseline = existingObl.BaselineVolume;
                    LogBaselineDecision(
                        group, "selected", "existing_obligation", baseline);
                }
                else
                {
                    var selection = group.SelectBaseline();
                    if (selection is not BaselineSelectionResult.Selected selected)
                    {
                        var reason = selection is BaselineSelectionResult.NoCandidates
                            ? "no_candidates"
                            : "volume_conflict";
                        LogBaselineDecision(
                            group, "rejected", reason, null);
                        continue;
                    }

                    baseline = selected.Baseline;
                    LogBaselineDecision(
                        group, "selected", "consistent_candidates", baseline);
                }

                existing = new ApplicationVolumeState(
                    group.Identity, baseline, isDucked: true);
                _stateStore.Add(existing);
                newlyTracked.Add(group.Identity);
                _logger?.Info($"Duck: {group.Identity} baseline={baseline:F2} kind=newly_tracked");
            }
            else
            {
                LogBaselineDecision(
                    group, "selected", "existing_state", existing!.BaselineVolume);
            }

            if (existing is not null && !existing.IsDucked)
                existing.SetDucked(true);

            newObligations.Add(new RestorationObligation(
                group.Identity,
                existing!.BaselineVolume,
                RestorationStatus.Ducked,
                now,
                now));
            acceptedGroups.Add(group);
        }

        if (acceptedGroups.Count == 0)
            return;

        var merged = MergeObligations(
            existingObligations,
            newObligations);

        var groupIdentities = acceptedGroups.Select(g => g.Identity).ToHashSet();

        for (var i = 0; i < merged.Count; i++)
        {
            if (merged[i].Status == RestorationStatus.Ducked
                && groupIdentities.Contains(merged[i].Identity))
            {
                merged[i] = new RestorationObligation(
                    merged[i].Identity,
                    merged[i].BaselineVolume,
                    RestorationStatus.RestorePending,
                    merged[i].CreatedAt,
                    now,
                    merged[i].SchemaVersion);
            }
        }

        try
        {
            SaveObligations(merged);
        }
        catch (Exception ex)
        {
            foreach (var snapshot in existingStateSnapshots.Values)
                _stateStore.Add(snapshot);
            foreach (var id in newlyTracked)
                _stateStore.Remove(id);
            foreach (var group in acceptedGroups)
                _logger?.Error($"DuckPersistPreWrite: {group.Identity} result=Failed error={ex.Message}");
            return;
        }

        var allWriteSucceeded = new List<ApplicationAudioIdentity>();

        foreach (var group in acceptedGroups)
        {
            if (!_stateStore.TryGet(group.Identity, out var state) || state is null)
                continue;

            var target = settings.Policy.ComputeDuckedVolume(state.BaselineVolume);
            var allSucceeded = true;

            foreach (var session in group.Sessions)
            {
                var result = _volumeWriter.SetVolume(session.Identity, target);
                if (result != VolumeWriteResult.Succeeded)
                {
                    _logger?.Warn($"DuckWrite: {group.Identity} session={session.Identity} target={target:F2} result={result}");
                    allSucceeded = false;
                }
            }

            if (allSucceeded)
            {
                for (var i = 0; i < merged.Count; i++)
                {
                    if (merged[i].Identity.Equals(group.Identity)
                        && merged[i].Status == RestorationStatus.RestorePending)
                    {
                        merged[i] = new RestorationObligation(
                            merged[i].Identity,
                            merged[i].BaselineVolume,
                            RestorationStatus.Ducked,
                            merged[i].CreatedAt,
                            DateTimeOffset.UtcNow,
                            merged[i].SchemaVersion);
                        break;
                    }
                }

                allWriteSucceeded.Add(group.Identity);
            }
        }

        try
        {
            SaveObligations(merged);
            var newDuckResult = allWriteSucceeded.ToHashSet();
            if (!newDuckResult.SetEquals(_lastDuckResult))
            {
                foreach (var id in allWriteSucceeded)
                    if (!_lastDuckResult.Contains(id))
                        _logger?.Info($"DuckResult: {id} result=Succeeded");
                _lastDuckResult = newDuckResult;
            }
        }
        catch (Exception ex)
        {
            _logger?.Error($"DuckPersist: {ex.Message}");
        }
    }

    private void LogEligibilityRejections(
        Dictionary<EligibilityDiagnosticKey, ControlEligibilityRejectionReason> currentRejections)
    {
        foreach (var (sessionKey, reason) in currentRejections)
        {
            if (_lastEligibilityRejections.TryGetValue(sessionKey, out var previous)
                && previous == reason)
            {
                continue;
            }

            _logger?.Info(
                $"ControlEligibility: session={sessionKey} eligible=false reason={reason}");
        }

        _lastEligibilityRejections = currentRejections;
    }

    private sealed record EligibilityDiagnosticKey(
        uint ProcessId,
        string ProcessName,
        string RenderDeviceId,
        string SessionInstanceIdentifier,
        string? ExecutablePath)
    {
        public static EligibilityDiagnosticKey From(AudioSessionInfo session)
        {
            var identity = session.Identity;
            return new EligibilityDiagnosticKey(
                identity.ProcessId,
                identity.ProcessName,
                identity.RenderDeviceId,
                identity.SessionInstanceIdentifier,
                session.ExecutablePath);
        }

        public override string ToString() =>
            $"pid={ProcessId},name={ProcessName},device={RenderDeviceId},instance={SessionInstanceIdentifier},path={ExecutablePath ?? "(null)"}";
    }

    private void LogBaselineDecision(
        ApplicationAudioSessionGroup group,
        string outcome,
        string reason,
        float? baseline)
    {
        var candidates = group.Sessions.Select(session => session.Volume).OrderBy(value => value).ToArray();
        var spread = candidates.Length == 0 ? 0f : candidates.Max() - candidates.Min();
        var candidateText = string.Join(
            ",",
            candidates.Select(value => value.ToString("R", CultureInfo.InvariantCulture)));
        var baselineFingerprint = baseline?.ToString("R", CultureInfo.InvariantCulture) ?? "none";
        var fingerprint = $"{outcome}|{reason}|{candidateText}|{spread:R}|{baselineFingerprint}";

        if (_lastBaselineDecisions.TryGetValue(group.Identity, out var previous)
            && string.Equals(previous, fingerprint, StringComparison.Ordinal))
        {
            return;
        }

        _lastBaselineDecisions[group.Identity] = fingerprint;

        var selectedBaseline = baseline is null
            ? "none"
            : baseline.Value.ToString("R", CultureInfo.InvariantCulture);
        _logger?.Info(
            $"BaselineDecision: identity={group.Identity.RenderDeviceId}|{group.Identity.ExecutablePath} candidates=[{candidateText}] spread={spread.ToString("R", CultureInfo.InvariantCulture)} outcome={outcome} reason={reason} baseline={selectedBaseline} endpoint_relevance=relevant relevance_reason=default_multimedia");
    }

    private void LogDuplicateObligationDecision(
        ApplicationAudioIdentity identity,
        IEnumerable<float> baselineCandidates)
    {
        var candidates = baselineCandidates.OrderBy(value => value).ToArray();
        var spread = candidates[^1] - candidates[0];
        var candidateText = string.Join(
            ",",
            candidates.Select(value => value.ToString("R", CultureInfo.InvariantCulture)));
        var fingerprint = $"rejected|duplicate_obligation_conflict|{candidateText}|{spread:R}|none";

        if (_lastBaselineDecisions.TryGetValue(identity, out var previous)
            && string.Equals(previous, fingerprint, StringComparison.Ordinal))
        {
            return;
        }

        _lastBaselineDecisions[identity] = fingerprint;
        _logger?.Warn(
            $"BaselineDecision: identity={identity.RenderDeviceId}|{identity.ExecutablePath} candidates=[{candidateText}] spread={spread.ToString("R", CultureInfo.InvariantCulture)} outcome=rejected reason=duplicate_obligation_conflict baseline=none endpoint_relevance=unknown relevance_reason=durable_repository_validation");
    }

    private sealed record DuplicateObligation(
        ApplicationAudioIdentity Identity,
        IReadOnlyList<float> Baselines);

    private static IReadOnlyList<DuplicateObligation> FindDuplicateObligations(
        IEnumerable<RestorationObligation> obligations)
    {
        return obligations
            .GroupBy(obligation => obligation.Identity)
            .Where(group => group.Count() > 1)
            .OrderBy(group => group.Key.RenderDeviceId, StringComparer.Ordinal)
            .ThenBy(group => group.Key.ExecutablePath, StringComparer.OrdinalIgnoreCase)
            .Select(group => new DuplicateObligation(
                group.Key,
                group.Select(obligation => obligation.BaselineVolume).ToArray()))
            .ToList();
    }

    private void PruneBaselineDecisions(IReadOnlySet<ApplicationAudioIdentity> retainedIdentities)
    {
        foreach (var staleIdentity in _lastBaselineDecisions.Keys
                     .Where(identity => !retainedIdentities.Contains(identity))
                     .ToList())
        {
            _lastBaselineDecisions.Remove(staleIdentity);
        }
    }

    private static RestorationObligation? FindObligation(
        IReadOnlyList<RestorationObligation> obligations,
        ApplicationAudioIdentity identity)
    {
        foreach (var obl in obligations)
        {
            if (obl.Identity.Equals(identity))
                return obl;
        }
        return null;
    }

    private static List<RestorationObligation> MergeObligations(
        IReadOnlyList<RestorationObligation> existing,
        IReadOnlyList<RestorationObligation> added)
    {
        var merged = existing
            .Where(obligation => obligation.Identity.IsResolved)
            .ToList();

        foreach (var obl in added)
        {
            var entry = obl;
            var existingIndex = merged.FindIndex(candidate =>
                candidate.Identity.Equals(obl.Identity));

            if (existingIndex >= 0
                && merged[existingIndex] is { } existingObl
                && (existingObl.Status == RestorationStatus.Ducked
                    || existingObl.Status == RestorationStatus.RestorePending))
            {
                entry = new RestorationObligation(
                    obl.Identity,
                    existingObl.BaselineVolume,
                    obl.Status,
                    existingObl.CreatedAt,
                    obl.UpdatedAt,
                    obl.SchemaVersion);
            }

            if (existingIndex >= 0)
                merged[existingIndex] = entry;
            else
                merged.Add(entry);
        }

        return merged;
    }

    public void RestoreVolumes(IReadOnlyList<AudioSessionInfo> currentSessions)
    {
        currentSessions = ApplyEndpointFilter(currentSessions);
        var restored = new List<ApplicationAudioIdentity>();
        var restoreFailed = new List<ApplicationAudioIdentity>();

        foreach (var state in _stateStore.GetAll())
        {
            if (!state.IsDucked)
                continue;

            var matchingSessions = currentSessions
                .Where(s => s.Identity.IsResolved &&
                            !string.IsNullOrEmpty(s.ExecutablePath) &&
                            new ApplicationAudioIdentity(
                                s.Identity.RenderDeviceId, s.ExecutablePath)
                                .Equals(state.Identity))
                .ToList();

            if (matchingSessions.Count == 0)
            {
                _logger?.Info($"Restore: {state.Identity} baseline={state.BaselineVolume:F2} result=SessionNotFound");
                restoreFailed.Add(state.Identity);
                continue;
            }

            var allSucceeded = true;
            var hasFailed = false;

            foreach (var session in matchingSessions)
            {
                var result = _volumeWriter.SetVolume(session.Identity, state.BaselineVolume);
                _logger?.Info($"RestoreWrite: {state.Identity} session={session.Identity} target={state.BaselineVolume:F2} result={result}");
                if (result == VolumeWriteResult.SessionNotFound)
                {
                    // logged per-session above; identity-level result derived below
                }
                else if (result != VolumeWriteResult.Succeeded)
                    hasFailed = true;
                if (result != VolumeWriteResult.Succeeded)
                    allSucceeded = false;
            }

            if (allSucceeded)
            {
                state.SetDucked(false);
                restored.Add(state.Identity);
                _logger?.Info($"Restore: {state.Identity} baseline={state.BaselineVolume:F2} result=Succeeded");
            }
            else if (hasFailed)
            {
                restoreFailed.Add(state.Identity);
                _logger?.Warn($"Restore: {state.Identity} baseline={state.BaselineVolume:F2} result=Failed");
            }
            else
            {
                restoreFailed.Add(state.Identity);
                _logger?.Info($"Restore: {state.Identity} baseline={state.BaselineVolume:F2} result=SessionNotFound");
            }
        }

        AddCleanupCandidates(restored);

        if (SyncObligationsAfterRestore(restored, restoreFailed))
        {
            foreach (var identity in restored)
                _stateStore.Remove(identity);
        }
    }

    private void AddCleanupCandidates(List<ApplicationAudioIdentity> restored)
    {
        var loadResult = LoadObligations();
        if (loadResult.WasCorrupt)
            return;

        var candidates = CollectCleanupCandidates(loadResult.Obligations);
        restored.AddRange(candidates);
    }

    private List<ApplicationAudioIdentity> CollectCleanupCandidates(
    IReadOnlyList<RestorationObligation> obligations)
    {
        var candidates = new List<ApplicationAudioIdentity>();

        foreach (var state in _stateStore.GetAll())
        {
            if (state.IsDucked)
                continue;

            for (var i = 0; i < obligations.Count; i++)
            {
                if (obligations[i].Identity.Equals(state.Identity))
                {
                    candidates.Add(state.Identity);
                    break;
                }
            }
        }

        return candidates;
    }

    public void ApplyDeferredRestores(IReadOnlyList<AudioSessionInfo> sessions)
    {
        sessions = ApplyEndpointFilter(sessions);
        var restored = new List<ApplicationAudioIdentity>();
        var restoreFailed = new List<ApplicationAudioIdentity>();
        var currentNotFound = new HashSet<ApplicationAudioIdentity>();

        foreach (var state in _stateStore.GetAll())
        {
            if (!state.IsDucked)
                continue;

            var matchingSessions = sessions
                .Where(s => s.Identity.IsResolved &&
                            !string.IsNullOrEmpty(s.ExecutablePath) &&
                            new ApplicationAudioIdentity(
                                s.Identity.RenderDeviceId, s.ExecutablePath)
                                .Equals(state.Identity))
                .ToList();

            if (matchingSessions.Count == 0)
            {
                currentNotFound.Add(state.Identity);
                if (!_lastDeferredNotFound.Contains(state.Identity))
                    _logger?.Info($"DeferredRestore: {state.Identity} baseline={state.BaselineVolume:F2} result=SessionNotFound");
                restoreFailed.Add(state.Identity);
                continue;
            }

            var allSucceeded = true;
            var hasFailed = false;

            foreach (var session in matchingSessions)
            {
                var result = _volumeWriter.SetVolume(session.Identity, state.BaselineVolume);
                if (result == VolumeWriteResult.SessionNotFound)
                    currentNotFound.Add(state.Identity);
                _logger?.Info($"DeferredRestoreWrite: {state.Identity} session={session.Identity} target={state.BaselineVolume:F2} result={result}");
                if (result != VolumeWriteResult.Succeeded)
                {
                    if (result != VolumeWriteResult.SessionNotFound)
                        hasFailed = true;
                    allSucceeded = false;
                }
            }

            if (allSucceeded)
            {
                state.SetDucked(false);
                restored.Add(state.Identity);
                _logger?.Info($"DeferredRestore: {state.Identity} baseline={state.BaselineVolume:F2} result=Succeeded");
            }
            else if (hasFailed)
            {
                restoreFailed.Add(state.Identity);
                _logger?.Warn($"DeferredRestore: {state.Identity} baseline={state.BaselineVolume:F2} result=Failed");
            }
            else
            {
                restoreFailed.Add(state.Identity);
                var deduped = _lastDeferredNotFound.Contains(state.Identity);
                if (!deduped)
                    _logger?.Info($"DeferredRestore: {state.Identity} baseline={state.BaselineVolume:F2} result=SessionNotFound");
            }
        }

        AddCleanupCandidates(restored);
        _lastDeferredNotFound = currentNotFound;

        if (SyncObligationsAfterRestore(restored, restoreFailed))
        {
            foreach (var identity in restored)
                _stateStore.Remove(identity);
        }
    }

    private bool SyncObligationsAfterRestore(
        IReadOnlyList<ApplicationAudioIdentity> restored,
        IReadOnlyList<ApplicationAudioIdentity> failed)
    {
        var loadResult = LoadObligations();
        if (loadResult.WasCorrupt)
            return false;

        var obligations = loadResult.Obligations
            .Where(o => o.Identity.IsResolved)
            .ToList();

        var restoredSet = restored.ToHashSet();
        var now = DateTimeOffset.UtcNow;

        var removedIdentities = new List<ApplicationAudioIdentity>();
        obligations.RemoveAll(o =>
        {
            if (restoredSet.Contains(o.Identity))
            {
                removedIdentities.Add(o.Identity);
                return true;
            }
            return false;
        });

        var dirty = removedIdentities.Count > 0;
        foreach (var id in failed)
        {
            for (var i = 0; i < obligations.Count; i++)
            {
                if (obligations[i].Identity.Equals(id))
                {
                    if (obligations[i].Status != RestorationStatus.RestorePending)
                    {
                        _logger?.Info($"Obligation: {id} action=retained reason=RestorePending");
                        obligations[i] = new RestorationObligation(
                            obligations[i].Identity,
                            obligations[i].BaselineVolume,
                            RestorationStatus.RestorePending,
                            obligations[i].CreatedAt,
                            now,
                            obligations[i].SchemaVersion);
                        _lastAlreadyPending.Remove(id);
                        dirty = true;
                    }
                    else if (!_lastAlreadyPending.Contains(id))
                    {
                        _lastAlreadyPending.Add(id);
                        _logger?.Info($"Obligation: {id} action=retained reason=already_pending");
                    }
                    break;
                }
            }
        }

        if (!dirty)
            return true;

        try
        {
            SaveObligations(obligations);
            foreach (var id in removedIdentities)
            {
                _lastAlreadyPending.Remove(id);
                _logger?.Info($"Obligation: {id} action=deleted");
            }
            return true;
        }
        catch (Exception ex)
        {
            _logger?.Error($"ObligationSave: {ex.Message}");
            return false;
        }
    }
}
