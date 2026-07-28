namespace VoiceDuck.Core;

public sealed class ApplicationAudioSessionGroup
{
    public ApplicationAudioIdentity Identity { get; }
    public IReadOnlyList<AudioSessionInfo> Sessions { get; }

    public ApplicationAudioSessionGroup(ApplicationAudioIdentity identity, IReadOnlyList<AudioSessionInfo> sessions)
    {
        Identity = identity ?? throw new ArgumentNullException(nameof(identity));
        Sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
    }

    public BaselineSelectionResult SelectBaseline()
    {
        return BaselineSelectionPolicy.Select(Sessions.Select(s => s.Volume));
    }

    public static IReadOnlyList<ApplicationAudioSessionGroup> GroupSessions(
        IReadOnlyList<AudioSessionInfo> sessions,
        string voiceDuckProcessName,
        DuckingSessionClassifier classifier,
        VoiceDuckSettings settings,
        string? relevantEndpointId,
        Action<AudioSessionInfo, ControlEligibilityResult.Rejected>? onRejected = null)
    {
        var groups = new Dictionary<ApplicationAudioIdentity, List<AudioSessionInfo>>();

        foreach (var session in sessions)
        {
            var decision = classifier.Classify(
                session,
                relevantEndpointId,
                settings,
                voiceDuckProcessName);
            if (decision is ControlEligibilityResult.Rejected rejected)
            {
                onRejected?.Invoke(session, rejected);
                continue;
            }

            var appIdentity = new ApplicationAudioIdentity(
                session.Identity.RenderDeviceId, session.ExecutablePath!);

            if (!groups.ContainsKey(appIdentity))
                groups[appIdentity] = new List<AudioSessionInfo>();

            groups[appIdentity].Add(session);
        }

        return groups
            .Select(kvp => new ApplicationAudioSessionGroup(kvp.Key, kvp.Value))
            .ToList();
    }
}
