namespace VoiceDuck.Core;

public interface IAudioSessionVolumeWriter
{
    VolumeWriteResult SetVolume(AudioSessionIdentity identity, float volume);
}
