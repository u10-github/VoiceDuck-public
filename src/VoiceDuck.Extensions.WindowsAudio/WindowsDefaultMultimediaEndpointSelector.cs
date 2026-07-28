using VoiceDuck.Core;

namespace VoiceDuck.Extensions.WindowsAudio;

public class WindowsDefaultMultimediaEndpointSelector : IAudioEndpointSelector
{
    private readonly IAudioEndpointReader _reader;

    internal WindowsDefaultMultimediaEndpointSelector(IAudioEndpointReader reader)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    public WindowsDefaultMultimediaEndpointSelector()
        : this(new WindowsAudioEndpointReader())
    {
    }

    public string? GetDefaultMultimediaEndpointId()
    {
        try
        {
            return _reader.GetDefaultMultimediaEndpointId();
        }
        catch
        {
            return null;
        }
    }
}
