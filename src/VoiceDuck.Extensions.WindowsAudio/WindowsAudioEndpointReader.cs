using NAudio.CoreAudioApi;

namespace VoiceDuck.Extensions.WindowsAudio;

internal sealed class WindowsAudioEndpointReader : IAudioEndpointReader
{
    public string? GetDefaultMultimediaEndpointId()
    {
        using var enumerator = new MMDeviceEnumerator();
        using var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        return defaultDevice?.ID;
    }
}
