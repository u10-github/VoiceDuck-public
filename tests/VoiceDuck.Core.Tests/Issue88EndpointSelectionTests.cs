using System.Linq;
using VoiceDuck.Infrastructure;

namespace VoiceDuck.Core.Tests;

public class Issue88EndpointSelectionTests
{
    private const string DefaultEndpoint = "{DefaultMultimedia}";
    private const string NonDefaultEndpoint = "{NonDefault}";
    private const string DefaultExePath = @"C:\Program Files\App\app.exe";

    private static readonly VoiceDuckSettings DefaultSettings = new(
        new DuckingPolicy(0.5, 10),
        new[] { new TriggerApp("Discord.exe") },
        Array.Empty<ExcludeApp>());

    private static AudioSessionIdentity SessionId(uint pid, string name, string device) =>
        new(pid, name, device, $"inst-{pid}");

    private static AudioSessionInfo Session(uint pid, string name, float vol, string device, string? path = DefaultExePath) =>
        new(SessionId(pid, name, device), vol, false, path);

    private sealed class EndpointSelectorMock : IAudioEndpointSelector
    {
        public string? EndpointId { get; set; } = DefaultEndpoint;
        public bool ThrowOnCall { get; set; }
        public Exception? CustomException { get; set; }
        public string? GetDefaultMultimediaEndpointId()
        {
            if (ThrowOnCall)
                throw CustomException ?? new InvalidOperationException("selector failure");
            return EndpointId;
        }
    }

    private sealed class WriterMock : IAudioSessionVolumeWriter
    {
        public List<(AudioSessionIdentity Identity, float Volume)> Calls { get; } = new();
        public Func<AudioSessionIdentity, VolumeWriteResult>? ResultFor { get; set; }

        public VolumeWriteResult SetVolume(AudioSessionIdentity identity, float volume)
        {
            Calls.Add((identity, volume));
            return ResultFor?.Invoke(identity) ?? VolumeWriteResult.Succeeded;
        }
    }

    private sealed class ObligationMock : IRestorationObligationRepository
    {
        public List<RestorationObligation> Saved { get; private set; } = new();
        public bool WasCorrupt { get; set; }
        public List<RestorationObligation> Existing { get; set; } = new();

        public RestorationObligationLoadResult LoadAll()
        {
            return new RestorationObligationLoadResult(Existing.ToArray(), WasCorrupt);
        }

        public void SaveAll(IReadOnlyList<RestorationObligation> obligations)
        {
            Saved = new List<RestorationObligation>(obligations);
            Existing = new List<RestorationObligation>(obligations);
        }

        public void DeleteAll()
        {
            Existing.Clear();
            Saved.Clear();
        }
    }

    private sealed class LoggerMock : ILogger
    {
        public List<string> Messages { get; } = new();
        public void Info(string message) => Messages.Add(message);
        public void Warn(string message) => Messages.Add(message);
        public void Error(string message) => Messages.Add(message);
    }

    // ── AC-088-001: Single policy applied to duck, restore, deferred restore ──

    [Fact]
    public void ApplyDucking_filters_to_default_endpoint()
    {
        var writer = new WriterMock();
        var store = new ApplicationVolumeStateStore();
        var classifier = new DuckingSessionClassifier();
        var obligations = new ObligationMock();
        var selector = new EndpointSelectorMock { EndpointId = DefaultEndpoint };
        var service = new VolumeDuckingService(
            writer, classifier, store, obligations,
            endpointSelector: selector);

        var sessions = new[]
        {
            Session(200, "Chrome.exe", 1.0f, DefaultEndpoint),
            Session(201, "Chrome.exe", 0.5f, NonDefaultEndpoint),
        };
        service.ApplyDucking(sessions, DefaultSettings, "VoiceDuck.exe");

        var defaultKey = new ApplicationAudioIdentity(DefaultEndpoint, DefaultExePath);
        var nonDefaultKey = new ApplicationAudioIdentity(NonDefaultEndpoint, DefaultExePath);
        Assert.True(store.TryGet(defaultKey, out _), "Default endpoint session should create state");
        Assert.False(store.TryGet(nonDefaultKey, out _), "Non-default endpoint session should not create state");
        Assert.Single(writer.Calls);
        Assert.Equal(DefaultEndpoint, writer.Calls[0].Identity.RenderDeviceId);
    }

    [Fact]
    public void RestoreVolumes_only_writes_to_default_endpoint()
    {
        var writer = new WriterMock();
        var store = new ApplicationVolumeStateStore();
        var classifier = new DuckingSessionClassifier();
        var obligations = new ObligationMock();
        var selector = new EndpointSelectorMock { EndpointId = DefaultEndpoint };
        var service = new VolumeDuckingService(
            writer, classifier, store, obligations,
            endpointSelector: selector);

        var defaultKey = new ApplicationAudioIdentity(DefaultEndpoint, DefaultExePath);
        store.Add(new ApplicationVolumeState(defaultKey, 0.8f, isDucked: true));

        var sessions = new[]
        {
            Session(200, "Chrome.exe", 0.4f, DefaultEndpoint),
            Session(201, "Chrome.exe", 0.3f, NonDefaultEndpoint),
        };
        service.RestoreVolumes(sessions);

        Assert.False(store.TryGet(defaultKey, out _), "Default endpoint state should be restored");
        Assert.Single(writer.Calls);
        Assert.Equal(DefaultEndpoint, writer.Calls[0].Identity.RenderDeviceId);
        Assert.Equal(0.8f, writer.Calls[0].Volume, 3);
    }

    [Fact]
    public void ApplyDeferredRestores_only_writes_to_default_endpoint()
    {
        var writer = new WriterMock();
        var store = new ApplicationVolumeStateStore();
        var classifier = new DuckingSessionClassifier();
        var obligations = new ObligationMock();
        var selector = new EndpointSelectorMock { EndpointId = DefaultEndpoint };
        var service = new VolumeDuckingService(
            writer, classifier, store, obligations,
            endpointSelector: selector);

        var defaultKey = new ApplicationAudioIdentity(DefaultEndpoint, DefaultExePath);
        store.Add(new ApplicationVolumeState(defaultKey, 0.8f, isDucked: true));

        var sessions = new[]
        {
            Session(200, "Chrome.exe", 0.4f, DefaultEndpoint),
            Session(201, "Chrome.exe", 0.3f, NonDefaultEndpoint),
        };
        service.ApplyDeferredRestores(sessions);

        Assert.False(store.TryGet(defaultKey, out _), "Default endpoint state should be restored");
        Assert.Single(writer.Calls);
        Assert.Equal(DefaultEndpoint, writer.Calls[0].Identity.RenderDeviceId);
        Assert.Equal(0.8f, writer.Calls[0].Volume, 3);
    }

    // ── AC-088-002: Non-default endpoint sessions excluded ──

    [Fact]
    public void Same_exe_non_default_stale_volume_excluded()
    {
        var writer = new WriterMock();
        var store = new ApplicationVolumeStateStore();
        var classifier = new DuckingSessionClassifier();
        var obligations = new ObligationMock();
        var selector = new EndpointSelectorMock { EndpointId = DefaultEndpoint };
        var service = new VolumeDuckingService(
            writer, classifier, store, obligations,
            endpointSelector: selector);

        var sessions = new[]
        {
            Session(200, "Chrome.exe", 1.0f, DefaultEndpoint),
            Session(201, "Chrome.exe", 0.5f, NonDefaultEndpoint),
        };
        service.ApplyDucking(sessions, DefaultSettings, "VoiceDuck.exe");

        var defaultKey = new ApplicationAudioIdentity(DefaultEndpoint, DefaultExePath);
        Assert.True(store.TryGet(defaultKey, out var state));
        Assert.Equal(1.0f, state!.BaselineVolume, 3);
        Assert.Single(writer.Calls);
        Assert.Equal(0.5f, writer.Calls[0].Volume, 3);
    }

    // ── AC-088-003: Lookup failure fails closed ──

    [Fact]
    public void Lookup_failure_fails_closed_no_duck()
    {
        var writer = new WriterMock();
        var store = new ApplicationVolumeStateStore();
        var classifier = new DuckingSessionClassifier();
        var obligations = new ObligationMock();
        var selector = new EndpointSelectorMock { EndpointId = null };
        var service = new VolumeDuckingService(
            writer, classifier, store, obligations,
            endpointSelector: selector);

        var sessions = new[]
        {
            Session(200, "Chrome.exe", 1.0f, DefaultEndpoint),
        };
        service.ApplyDucking(sessions, DefaultSettings, "VoiceDuck.exe");

        Assert.Empty(store.GetAll());
        Assert.Empty(writer.Calls);
    }

    [Fact]
    public void Lookup_failure_fails_closed_no_restore()
    {
        var writer = new WriterMock();
        var store = new ApplicationVolumeStateStore();
        var classifier = new DuckingSessionClassifier();
        var obligations = new ObligationMock();
        var selector = new EndpointSelectorMock { EndpointId = null };
        var service = new VolumeDuckingService(
            writer, classifier, store, obligations,
            endpointSelector: selector);

        var defaultKey = new ApplicationAudioIdentity(DefaultEndpoint, DefaultExePath);
        store.Add(new ApplicationVolumeState(defaultKey, 0.8f, isDucked: true));

        var sessions = new[]
        {
            Session(200, "Chrome.exe", 0.4f, DefaultEndpoint),
        };
        service.RestoreVolumes(sessions);

        Assert.Empty(writer.Calls);
    }

    [Fact]
    public void Lookup_failure_fails_closed_no_deferred_restore()
    {
        var writer = new WriterMock();
        var store = new ApplicationVolumeStateStore();
        var classifier = new DuckingSessionClassifier();
        var obligations = new ObligationMock();
        var selector = new EndpointSelectorMock { EndpointId = null };
        var service = new VolumeDuckingService(
            writer, classifier, store, obligations,
            endpointSelector: selector);

        var defaultKey = new ApplicationAudioIdentity(DefaultEndpoint, DefaultExePath);
        store.Add(new ApplicationVolumeState(defaultKey, 0.8f, isDucked: true));

        var sessions = new[]
        {
            Session(200, "Chrome.exe", 0.4f, DefaultEndpoint),
        };
        service.ApplyDeferredRestores(sessions);

        Assert.Empty(writer.Calls);
    }

    [Fact]
    public void Throwing_selector_fails_closed_no_write()
    {
        var writer = new WriterMock();
        var store = new ApplicationVolumeStateStore();
        var classifier = new DuckingSessionClassifier();
        var obligations = new ObligationMock();
        var selector = new EndpointSelectorMock { ThrowOnCall = true };
        var log = new LoggerMock();
        var service = new VolumeDuckingService(
            writer, classifier, store, obligations,
            endpointSelector: selector,
            logger: log);

        var sessions = new[]
        {
            Session(200, "Chrome.exe", 1.0f, DefaultEndpoint),
        };
        service.ApplyDucking(sessions, DefaultSettings, "VoiceDuck.exe");

        Assert.Empty(store.GetAll());
        Assert.Empty(writer.Calls);
        Assert.Contains(log.Messages, m => m.Contains("lookup_failed"));
        Assert.Contains(log.Messages, m => m.Contains("selector failure"));
    }

    // ── AC-088-004: Durable obligations preserved when no relevant endpoint ──

    [Fact]
    public void Lookup_failure_preserves_existing_obligations()
    {
        var writer = new WriterMock();
        var store = new ApplicationVolumeStateStore();
        var classifier = new DuckingSessionClassifier();
        var obligations = new ObligationMock();
        var selector = new EndpointSelectorMock { EndpointId = null };
        var service = new VolumeDuckingService(
            writer, classifier, store, obligations,
            endpointSelector: selector);

        var defaultKey = new ApplicationAudioIdentity(DefaultEndpoint, DefaultExePath);
        obligations.Existing = new List<RestorationObligation>
        {
            new(defaultKey, 0.8f, RestorationStatus.Ducked,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
        };
        store.Add(new ApplicationVolumeState(defaultKey, 0.8f, isDucked: true));

        service.ApplyDucking(Array.Empty<AudioSessionInfo>(), DefaultSettings, "VoiceDuck.exe");

        Assert.True(store.TryGet(defaultKey, out _));
        Assert.Contains(obligations.Existing, o => o.Identity.Equals(defaultKey));
    }

    private static VolumeDuckingService CreateService(
        WriterMock? writer = null,
        LoggerMock? log = null,
        EndpointSelectorMock? selector = null)
    {
        return new VolumeDuckingService(
            writer ?? new WriterMock(),
            new DuckingSessionClassifier(),
            new ApplicationVolumeStateStore(),
            new ObligationMock(),
            endpointSelector: selector ?? new EndpointSelectorMock(),
            logger: log);
    }

    private static int EndpointSelectionCount(LoggerMock log) =>
        log.Messages.Count(m => m.StartsWith("EndpointSelection:"));

    // ── Endpoint diagnostics deduplication (PR #93 rework) ──

    [Fact]
    public void Repeated_success_emits_no_new_diagnostics()
    {
        var log = new LoggerMock();
        var selector = new EndpointSelectorMock { EndpointId = DefaultEndpoint };
        var service = CreateService(log: log, selector: selector);

        var sessions = new[]
        {
            Session(200, "Chrome.exe", 1.0f, DefaultEndpoint),
            Session(201, "Spotify.exe", 0.5f, NonDefaultEndpoint),
        };

        service.ApplyDucking(sessions, DefaultSettings, "VoiceDuck.exe");
        var afterFirst = EndpointSelectionCount(log);

        service.ApplyDucking(sessions, DefaultSettings, "VoiceDuck.exe");

        Assert.Equal(afterFirst, EndpointSelectionCount(log));
    }

    [Fact]
    public void Repeated_failure_emits_no_new_diagnostics()
    {
        var log = new LoggerMock();
        var selector = new EndpointSelectorMock { EndpointId = null };
        var service = CreateService(log: log, selector: selector);

        var sessions = new[]
        {
            Session(200, "Chrome.exe", 1.0f, DefaultEndpoint),
        };

        service.ApplyDucking(sessions, DefaultSettings, "VoiceDuck.exe");
        Assert.Contains(log.Messages, m => m.Contains("reason=lookup_failed"));
        var afterFirst = EndpointSelectionCount(log);

        service.ApplyDucking(sessions, DefaultSettings, "VoiceDuck.exe");

        Assert.Equal(afterFirst, EndpointSelectionCount(log));
    }

    [Fact]
    public void Failure_to_success_transition_logs_both()
    {
        var log = new LoggerMock();
        var selector = new EndpointSelectorMock { EndpointId = null };
        var service = CreateService(log: log, selector: selector);

        var sessions = new[]
        {
            Session(200, "Chrome.exe", 1.0f, DefaultEndpoint),
        };

        service.ApplyDucking(sessions, DefaultSettings, "VoiceDuck.exe");
        var afterFail = EndpointSelectionCount(log);
        Assert.Contains(log.Messages, m => m.Contains("reason=lookup_failed"));

        selector.EndpointId = DefaultEndpoint;
        service.ApplyDucking(sessions, DefaultSettings, "VoiceDuck.exe");

        Assert.Equal(afterFail + 1, EndpointSelectionCount(log));
        Assert.Contains(log.Messages, m =>
            m.Contains("selected=true") && m.Contains("reason=default_multimedia"));
    }

    [Fact]
    public void Success_to_failure_transition_logs_both()
    {
        var log = new LoggerMock();
        var selector = new EndpointSelectorMock { EndpointId = DefaultEndpoint };
        var service = CreateService(log: log, selector: selector);

        var sessions = new[]
        {
            Session(200, "Chrome.exe", 1.0f, DefaultEndpoint),
        };

        service.ApplyDucking(sessions, DefaultSettings, "VoiceDuck.exe");
        var afterSuccess = EndpointSelectionCount(log);
        Assert.Contains(log.Messages, m =>
            m.Contains("selected=true") && m.Contains("reason=default_multimedia"));

        selector.EndpointId = null;
        service.ApplyDucking(sessions, DefaultSettings, "VoiceDuck.exe");

        Assert.Equal(afterSuccess + 1, EndpointSelectionCount(log));
        Assert.Contains(log.Messages, m => m.Contains("reason=lookup_failed"));
    }

    [Fact]
    public void Changed_default_endpoint_logs_new_selection()
    {
        var log = new LoggerMock();
        var selector = new EndpointSelectorMock { EndpointId = DefaultEndpoint };
        var service = CreateService(log: log, selector: selector);

        var sessions = new[]
        {
            Session(200, "Chrome.exe", 1.0f, DefaultEndpoint),
        };

        service.ApplyDucking(sessions, DefaultSettings, "VoiceDuck.exe");
        var afterFirst = EndpointSelectionCount(log);

        const string newDefault = "{NewDefault}";
        selector.EndpointId = newDefault;
        var newSessions = new[]
        {
            Session(200, "Chrome.exe", 1.0f, newDefault),
        };
        service.ApplyDucking(newSessions, DefaultSettings, "VoiceDuck.exe");

        Assert.Equal(afterFirst + 1, EndpointSelectionCount(log));
        Assert.Contains(log.Messages, m =>
            m.Contains($"endpoint={newDefault}") && m.Contains("selected=true"));
    }

    [Fact]
    public void Rejected_set_add_logs_new_rejections()
    {
        var log = new LoggerMock();
        var selector = new EndpointSelectorMock { EndpointId = DefaultEndpoint };
        var service = CreateService(log: log, selector: selector);

        var firstSessions = new[]
        {
            Session(200, "Chrome.exe", 1.0f, DefaultEndpoint),
            Session(201, "Spotify.exe", 0.5f, NonDefaultEndpoint),
        };
        service.ApplyDucking(firstSessions, DefaultSettings, "VoiceDuck.exe");
        var afterFirst = EndpointSelectionCount(log);

        const string anotherNonDefault = "{AnotherNonDefault}";
        var secondSessions = new[]
        {
            Session(200, "Chrome.exe", 1.0f, DefaultEndpoint),
            Session(201, "Spotify.exe", 0.5f, NonDefaultEndpoint),
            Session(202, "Discord.exe", 0.3f, anotherNonDefault),
        };
        service.ApplyDucking(secondSessions, DefaultSettings, "VoiceDuck.exe");

        var afterSecond = EndpointSelectionCount(log);
        Assert.True(afterSecond > afterFirst);
        Assert.Contains(log.Messages, m =>
            m.Contains($"endpoint={anotherNonDefault}") && m.Contains("selected=false"));
    }

    [Fact]
    public void Rejected_set_remove_logs_new_state()
    {
        var log = new LoggerMock();
        var selector = new EndpointSelectorMock { EndpointId = DefaultEndpoint };
        var service = CreateService(log: log, selector: selector);

        var firstSessions = new[]
        {
            Session(200, "Chrome.exe", 1.0f, DefaultEndpoint),
            Session(201, "Spotify.exe", 0.5f, NonDefaultEndpoint),
        };
        service.ApplyDucking(firstSessions, DefaultSettings, "VoiceDuck.exe");
        var afterFirst = EndpointSelectionCount(log);

        var secondSessions = new[]
        {
            Session(200, "Chrome.exe", 1.0f, DefaultEndpoint),
        };
        service.ApplyDucking(secondSessions, DefaultSettings, "VoiceDuck.exe");

        var afterSecond = EndpointSelectionCount(log);
        Assert.True(afterSecond > afterFirst);
    }

    [Fact]
    public void Repeated_identical_failure_produces_zero_volume_writes()
    {
        var log = new LoggerMock();
        var writer = new WriterMock();
        var selector = new EndpointSelectorMock { ThrowOnCall = true };
        var service = CreateService(writer: writer, log: log, selector: selector);

        var sessions = new[]
        {
            Session(200, "Chrome.exe", 1.0f, DefaultEndpoint),
        };

        service.ApplyDucking(sessions, DefaultSettings, "VoiceDuck.exe");
        Assert.Empty(writer.Calls);
        Assert.Contains(log.Messages, m => m.Contains("reason=lookup_failed") && m.Contains("error="));
        var afterFirst = EndpointSelectionCount(log);

        service.ApplyDucking(sessions, DefaultSettings, "VoiceDuck.exe");

        Assert.Empty(writer.Calls);
        Assert.Equal(afterFirst, EndpointSelectionCount(log));
    }

    [Fact]
    public void Changed_failure_content_logs_new_error()
    {
        var log = new LoggerMock();
        var selector = new EndpointSelectorMock { ThrowOnCall = true };
        var service = CreateService(log: log, selector: selector);

        var sessions = new[]
        {
            Session(200, "Chrome.exe", 1.0f, DefaultEndpoint),
        };

        service.ApplyDucking(sessions, DefaultSettings, "VoiceDuck.exe");
        var afterFirst = EndpointSelectionCount(log);
        Assert.Contains(log.Messages, m => m.Contains("selector failure"));

        selector.CustomException = new InvalidOperationException("different failure content");
        service.ApplyDucking(sessions, DefaultSettings, "VoiceDuck.exe");

        Assert.Equal(afterFirst + 1, EndpointSelectionCount(log));
        Assert.Contains(log.Messages, m => m.Contains("different failure content"));
    }

    [Fact]
    public void Changed_failure_kind_empty_to_exception_logs_new_error()
    {
        var log = new LoggerMock();
        var selector = new EndpointSelectorMock { EndpointId = null };
        var service = CreateService(log: log, selector: selector);

        var sessions = new[]
        {
            Session(200, "Chrome.exe", 1.0f, DefaultEndpoint),
        };

        service.ApplyDucking(sessions, DefaultSettings, "VoiceDuck.exe");
        var afterFirst = EndpointSelectionCount(log);
        Assert.Contains(log.Messages, m => m.Contains("reason=lookup_failed"));
        Assert.DoesNotContain(log.Messages, m => m.Contains("error="));

        selector.ThrowOnCall = true;
        selector.CustomException = new InvalidOperationException("throw failure");
        service.ApplyDucking(sessions, DefaultSettings, "VoiceDuck.exe");

        Assert.Equal(afterFirst + 1, EndpointSelectionCount(log));
        Assert.Contains(log.Messages, m => m.Contains("throw failure"));
    }

    [Fact]
    public void Reordered_sessions_same_rejected_set_no_new_diagnostics()
    {
        var log = new LoggerMock();
        var selector = new EndpointSelectorMock { EndpointId = DefaultEndpoint };
        var service = CreateService(log: log, selector: selector);

        var firstSessions = new[]
        {
            Session(201, "Spotify.exe", 0.5f, NonDefaultEndpoint),
            Session(200, "Chrome.exe", 1.0f, DefaultEndpoint),
        };
        service.ApplyDucking(firstSessions, DefaultSettings, "VoiceDuck.exe");
        var afterFirst = EndpointSelectionCount(log);
        Assert.True(afterFirst > 0);

        var reorderedSessions = new[]
        {
            Session(200, "Chrome.exe", 1.0f, DefaultEndpoint),
            Session(201, "Spotify.exe", 0.5f, NonDefaultEndpoint),
        };
        service.ApplyDucking(reorderedSessions, DefaultSettings, "VoiceDuck.exe");

        Assert.Equal(afterFirst, EndpointSelectionCount(log));
    }

    // ── AC-088-005: Diagnostics record endpoint ID and reason ──

    [Fact]
    public void Logger_reports_endpoint_selection_and_rejection_reasons()
    {
        var writer = new WriterMock();
        var store = new ApplicationVolumeStateStore();
        var classifier = new DuckingSessionClassifier();
        var obligations = new ObligationMock();
        var selector = new EndpointSelectorMock { EndpointId = DefaultEndpoint };
        var log = new LoggerMock();
        var service = new VolumeDuckingService(
            writer, classifier, store, obligations,
            endpointSelector: selector,
            logger: log);

        var sessions = new[]
        {
            Session(200, "Chrome.exe", 1.0f, DefaultEndpoint),
            Session(201, "Chrome.exe", 0.5f, NonDefaultEndpoint),
        };
        service.ApplyDucking(sessions, DefaultSettings, "VoiceDuck.exe");

        var selectedMsg = log.Messages.FirstOrDefault(m =>
            m.Contains("selected=true") && m.Contains("reason=default_multimedia"));
        Assert.NotNull(selectedMsg);
        Assert.Contains(DefaultEndpoint, selectedMsg);

        var rejectedMsg = log.Messages.FirstOrDefault(m =>
            m.Contains("selected=false") && m.Contains("reason=not_default_multimedia"));
        Assert.NotNull(rejectedMsg);
        Assert.Contains(NonDefaultEndpoint, rejectedMsg);
    }

    [Fact]
    public void Logger_reports_endpoint_lookup_failure_reason()
    {
        var writer = new WriterMock();
        var store = new ApplicationVolumeStateStore();
        var classifier = new DuckingSessionClassifier();
        var obligations = new ObligationMock();
        var selector = new EndpointSelectorMock { EndpointId = null };
        var log = new LoggerMock();
        var service = new VolumeDuckingService(
            writer, classifier, store, obligations,
            endpointSelector: selector,
            logger: log);

        service.ApplyDucking(Array.Empty<AudioSessionInfo>(), DefaultSettings, "VoiceDuck.exe");

        Assert.Contains(log.Messages, m => m.Contains("reason=lookup_failed"));
    }

    // ── Six-endpoint scenario (negative case from AO-088-001) ──

    // ── Empty/whitespace endpoint ID fails closed (Issue #88 round 4) ──

    [Fact]
    public void Empty_endpoint_id_fails_closed()
    {
        var writer = new WriterMock();
        var store = new ApplicationVolumeStateStore();
        var classifier = new DuckingSessionClassifier();
        var obligations = new ObligationMock();
        var selector = new EndpointSelectorMock { EndpointId = "" };
        var log = new LoggerMock();
        var service = new VolumeDuckingService(
            writer, classifier, store, obligations,
            endpointSelector: selector,
            logger: log);

        var sessions = new[]
        {
            Session(200, "Chrome.exe", 1.0f, ""),
        };
        service.ApplyDucking(sessions, DefaultSettings, "VoiceDuck.exe");

        Assert.Empty(store.GetAll());
        Assert.Empty(writer.Calls);
        Assert.Contains(log.Messages, m => m.Contains("reason=lookup_failed"));
    }

    [Fact]
    public void Whitespace_endpoint_id_fails_closed()
    {
        var writer = new WriterMock();
        var store = new ApplicationVolumeStateStore();
        var classifier = new DuckingSessionClassifier();
        var obligations = new ObligationMock();
        var selector = new EndpointSelectorMock { EndpointId = "   " };
        var log = new LoggerMock();
        var service = new VolumeDuckingService(
            writer, classifier, store, obligations,
            endpointSelector: selector,
            logger: log);

        var sessions = new[]
        {
            Session(200, "Chrome.exe", 1.0f, "   "),
        };
        service.ApplyDucking(sessions, DefaultSettings, "VoiceDuck.exe");

        Assert.Empty(store.GetAll());
        Assert.Empty(writer.Calls);
        Assert.Contains(log.Messages, m => m.Contains("reason=lookup_failed"));
    }

    [Fact]
    public void Six_active_endpoints_only_default_eligible()
    {
        var writer = new WriterMock();
        var store = new ApplicationVolumeStateStore();
        var classifier = new DuckingSessionClassifier();
        var obligations = new ObligationMock();
        var log = new LoggerMock();

        var defaultKey = new ApplicationAudioIdentity(DefaultEndpoint, DefaultExePath);
        var nonDefaultEndpoints = new[] { "nd-1", "nd-2", "nd-3", "nd-4", "nd-5" };

        var sessions = new List<AudioSessionInfo>();
        sessions.Add(Session(200, "Chrome.exe", 1.0f, DefaultEndpoint));
        foreach (var nd in nonDefaultEndpoints)
        {
            sessions.Add(Session(201, "Chrome.exe", 0.5f, nd));
        }

        Assert.Equal(6, sessions.Count);

        var selector = new EndpointSelectorMock { EndpointId = DefaultEndpoint };
        var service = new VolumeDuckingService(
            writer, classifier, store, obligations,
            endpointSelector: selector,
            logger: log);

        service.ApplyDucking(sessions, DefaultSettings, "VoiceDuck.exe");

        // Only default endpoint creates state and write
        Assert.True(store.TryGet(defaultKey, out var state));
        Assert.Equal(1.0f, state!.BaselineVolume, 3);
        Assert.Equal(1, store.Count);
        Assert.Single(writer.Calls);
        Assert.Equal(0.5f, writer.Calls[0].Volume, 3);

        // Every non-default endpoint ID was logged with reason=not_default_multimedia
        foreach (var nd in nonDefaultEndpoints)
        {
            Assert.Contains(log.Messages, m =>
                m.Contains($"endpoint={nd}") && m.Contains("selected=false") && m.Contains("reason=not_default_multimedia"));
        }

        // Default endpoint was logged with reason=default_multimedia
        Assert.Contains(log.Messages, m =>
            m.Contains($"endpoint={DefaultEndpoint}") && m.Contains("selected=true") && m.Contains("reason=default_multimedia"));
    }

    // ── Regression: RestoreVolumes/ApplyDeferredRestores with null/throw + recovery ──

    private static string CreateTempObligationFile(
        ApplicationAudioIdentity identity,
        float baselineVolume,
        RestorationStatus status,
        out RestorationObligationRepository repo)
    {
        var dir = Path.Combine(Path.GetTempPath(), "vd_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var filePath = Path.Combine(dir, "obligations.json");
        repo = new RestorationObligationRepository(filePath);
        var now = DateTimeOffset.UtcNow;
        repo.SaveAll(new[]
        {
            new RestorationObligation(identity, baselineVolume, status, now, now),
        });
        return dir;
    }

    public static IEnumerable<object[]> RestoreObligationCases()
    {
        // (useThrow, useDeferred)
        yield return new object[] { false, false }; // null selector, RestoreVolumes
        yield return new object[] { true, false };  // throw selector, RestoreVolumes
        yield return new object[] { false, true };  // null selector, ApplyDeferredRestores
        yield return new object[] { true, true };   // throw selector, ApplyDeferredRestores
    }

    [Theory]
    [MemberData(nameof(RestoreObligationCases))]
    public void Obligation_persisted_through_selector_failure_then_recovered(
        bool useThrow, bool useDeferred)
    {
        var writer = new WriterMock();
        var store = new ApplicationVolumeStateStore();
        var classifier = new DuckingSessionClassifier();
        var selector = new EndpointSelectorMock();

        var defaultKey = new ApplicationAudioIdentity(DefaultEndpoint, DefaultExePath);
        var tempDir = CreateTempObligationFile(
            defaultKey, 0.8f, RestorationStatus.Ducked, out var repo);
        try
        {
            store.Add(new ApplicationVolumeState(defaultKey, 0.8f, isDucked: true));

            var service = new VolumeDuckingService(
                writer, classifier, store, repo,
                endpointSelector: selector);

            var sessions = new[] { Session(200, "Chrome.exe", 0.4f, DefaultEndpoint) };

            // Phase 1: selector fails → no write, persisted obligation survives
            if (useThrow)
                selector.ThrowOnCall = true;
            else
                selector.EndpointId = null;

            if (useDeferred)
                service.ApplyDeferredRestores(sessions);
            else
                service.RestoreVolumes(sessions);

            Assert.Empty(writer.Calls);
            Assert.True(store.TryGet(defaultKey, out _),
                "State preserved on selector failure");

            var afterFail = repo.LoadAll();
            Assert.False(afterFail.WasCorrupt);
            Assert.Contains(afterFail.Obligations, o => o.Identity.Equals(defaultKey));

            // Phase 2: selector recovers → write succeeds, state and obligation removed
            writer.Calls.Clear();
            selector.ThrowOnCall = false;
            selector.EndpointId = DefaultEndpoint;

            if (useDeferred)
                service.ApplyDeferredRestores(sessions);
            else
                service.RestoreVolumes(sessions);

            Assert.Single(writer.Calls);
            Assert.Equal(0.8f, writer.Calls[0].Volume, 3);
            Assert.False(store.TryGet(defaultKey, out _),
                "State removed after recovery");

            var afterRecover = repo.LoadAll();
            Assert.False(afterRecover.WasCorrupt);
            Assert.DoesNotContain(afterRecover.Obligations, o => o.Identity.Equals(defaultKey));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }
}
