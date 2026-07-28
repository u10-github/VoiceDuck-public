namespace VoiceDuck.Core;

public interface IAudioEndpointSelector
{
    string? GetDefaultMultimediaEndpointId();
}
