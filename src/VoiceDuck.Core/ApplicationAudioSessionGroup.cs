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

    public float BaselineFromMaxVolume()
    {
        return Sessions.Max(s => s.Volume);
    }

    public static IReadOnlyList<ApplicationAudioSessionGroup> GroupSessions(
        IReadOnlyList<AudioSessionInfo> sessions,
        string voiceDuckProcessName,
        DuckingSessionClassifier classifier,
        VoiceDuckSettings settings)
    {
        var groups = new Dictionary<ApplicationAudioIdentity, List<AudioSessionInfo>>();

        foreach (var session in sessions)
        {
            if (session.Identity.ProcessId == 0 || !session.Identity.IsResolved)
                continue;

            if (string.IsNullOrEmpty(session.ExecutablePath))
                continue;

            var decision = classifier.Classify(session, settings, voiceDuckProcessName);
            if (decision.Outcome == DuckingOutcome.Protect)
                continue;

            var appIdentity = new ApplicationAudioIdentity(
                session.Identity.RenderDeviceId, session.ExecutablePath);

            if (!groups.ContainsKey(appIdentity))
                groups[appIdentity] = new List<AudioSessionInfo>();

            groups[appIdentity].Add(session);
        }

        return groups
            .Select(kvp => new ApplicationAudioSessionGroup(kvp.Key, kvp.Value))
            .ToList();
    }
}
