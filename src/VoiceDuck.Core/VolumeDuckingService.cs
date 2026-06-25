namespace VoiceDuck.Core;

public class VolumeDuckingService
{
    private readonly IAudioSessionVolumeWriter _volumeWriter;
    private readonly DuckingSessionClassifier _classifier;
    private readonly VolumeSnapshotStore _snapshotStore;

    public VolumeDuckingService(
        IAudioSessionVolumeWriter volumeWriter,
        DuckingSessionClassifier classifier,
        VolumeSnapshotStore snapshotStore)
    {
        _volumeWriter = volumeWriter ?? throw new ArgumentNullException(nameof(volumeWriter));
        _classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
        _snapshotStore = snapshotStore ?? throw new ArgumentNullException(nameof(snapshotStore));
    }

    public void ApplyDucking(
        IReadOnlyList<AudioSessionInfo> sessions,
        VoiceDuckSettings settings,
        string voiceDuckProcessName)
    {
        foreach (var session in sessions)
        {
            if (session.Identity.ProcessId == 0 || !session.Identity.IsResolved)
                continue;

            var decision = _classifier.Classify(session, settings, voiceDuckProcessName);
            if (decision.Outcome == DuckingOutcome.Protect)
                continue;

            if (!_snapshotStore.Contains(session.Identity))
            {
                _snapshotStore.Add(new VolumeSnapshot(session.Identity, session.Volume));
            }

            _snapshotStore.TryGet(session.Identity, out var snapshot);
            var duckedVolume = settings.Policy.ComputeDuckedVolume(snapshot!.OriginalVolume);
            _volumeWriter.SetVolume(session.Identity, duckedVolume);
        }
    }

    public void RestoreVolumes()
    {
        foreach (var snapshot in _snapshotStore.GetAll())
        {
            _volumeWriter.SetVolume(snapshot.SessionIdentity, snapshot.OriginalVolume);
        }

        _snapshotStore.Clear();
    }
}
