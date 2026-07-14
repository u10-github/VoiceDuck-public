namespace VoiceDuck.Core;

public sealed class ApplicationVolumeState
{
    public ApplicationAudioIdentity Identity { get; }
    public float BaselineVolume { get; }
    public bool IsDucked { get; private set; }

    public ApplicationVolumeState(ApplicationAudioIdentity identity, float baselineVolume, bool isDucked)
    {
        Identity = identity ?? throw new ArgumentNullException(nameof(identity));
        BaselineVolume = baselineVolume;
        IsDucked = isDucked;
    }

    public void SetDucked(bool ducked)
    {
        IsDucked = ducked;
    }
}
