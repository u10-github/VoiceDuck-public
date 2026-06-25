namespace VoiceDuck.Core;

public interface IAudioSessionVolumeWriter
{
    void SetVolume(AudioSessionIdentity identity, float volume);
}
