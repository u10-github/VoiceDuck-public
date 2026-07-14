namespace VoiceDuck.Core;

public class VolumeDuckingService
{
    private readonly IAudioSessionVolumeWriter _volumeWriter;
    private readonly DuckingSessionClassifier _classifier;
    private readonly ApplicationVolumeStateStore _stateStore;

    public VolumeDuckingService(
        IAudioSessionVolumeWriter volumeWriter,
        DuckingSessionClassifier classifier,
        ApplicationVolumeStateStore stateStore)
    {
        _volumeWriter = volumeWriter ?? throw new ArgumentNullException(nameof(volumeWriter));
        _classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
    }

    public void ApplyDucking(
        IReadOnlyList<AudioSessionInfo> sessions,
        VoiceDuckSettings settings,
        string voiceDuckProcessName)
    {
        var groups = ApplicationAudioSessionGroup.GroupSessions(
            sessions, voiceDuckProcessName, _classifier, settings);

        foreach (var group in groups)
        {
            if (!_stateStore.TryGet(group.Identity, out var existing) || existing is null)
            {
                var baseline = group.BaselineFromMaxVolume();
                existing = new ApplicationVolumeState(
                    group.Identity, baseline, isDucked: true);
                _stateStore.Add(existing);
            }

            if (!existing.IsDucked)
                existing.SetDucked(true);

            var target = settings.Policy.ComputeDuckedVolume(existing.BaselineVolume);

            foreach (var session in group.Sessions)
            {
                _volumeWriter.SetVolume(session.Identity, target);
            }
        }
    }

    public void RestoreVolumes(IReadOnlyList<AudioSessionInfo> currentSessions)
    {
        var toRemove = new List<ApplicationAudioIdentity>();

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
                continue;

            var allSucceeded = true;

            foreach (var session in matchingSessions)
            {
                var result = _volumeWriter.SetVolume(session.Identity, state.BaselineVolume);
                if (result != VolumeWriteResult.Succeeded)
                    allSucceeded = false;
            }

            if (allSucceeded)
                toRemove.Add(state.Identity);
        }

        foreach (var identity in toRemove)
            _stateStore.Remove(identity);
    }

    public void ApplyDeferredRestores(IReadOnlyList<AudioSessionInfo> sessions)
    {
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
                continue;

            var allSucceeded = true;

            foreach (var session in matchingSessions)
            {
                var result = _volumeWriter.SetVolume(session.Identity, state.BaselineVolume);
                if (result != VolumeWriteResult.Succeeded)
                    allSucceeded = false;
            }

            if (allSucceeded)
                _stateStore.Remove(state.Identity);
        }
    }
}
