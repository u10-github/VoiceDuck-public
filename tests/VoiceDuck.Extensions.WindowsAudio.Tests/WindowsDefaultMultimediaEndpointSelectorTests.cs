using VoiceDuck.Extensions.WindowsAudio;

namespace VoiceDuck.Extensions.WindowsAudio.Tests;

public class WindowsDefaultMultimediaEndpointSelectorTests
{
    [Fact]
    public void GetDefaultMultimediaEndpointId_returns_reader_result_on_success()
    {
        var reader = new EndpointReaderMock { Result = "device-{00000000-0000-0000-0000-000000000000}" };
        var selector = new WindowsDefaultMultimediaEndpointSelector(reader);

        var result = selector.GetDefaultMultimediaEndpointId();

        Assert.Equal("device-{00000000-0000-0000-0000-000000000000}", result);
    }

    [Fact]
    public void GetDefaultMultimediaEndpointId_returns_null_when_reader_returns_null()
    {
        var reader = new EndpointReaderMock { Result = null };
        var selector = new WindowsDefaultMultimediaEndpointSelector(reader);

        var result = selector.GetDefaultMultimediaEndpointId();

        Assert.Null(result);
    }

    [Fact]
    public void GetDefaultMultimediaEndpointId_returns_null_when_reader_throws()
    {
        var reader = new EndpointReaderMock { ThrowOnCall = true };
        var selector = new WindowsDefaultMultimediaEndpointSelector(reader);

        var result = selector.GetDefaultMultimediaEndpointId();

        Assert.Null(result);
    }

    private sealed class EndpointReaderMock : IAudioEndpointReader
    {
        public string? Result { get; set; }
        public bool ThrowOnCall { get; set; }

        public string? GetDefaultMultimediaEndpointId()
        {
            if (ThrowOnCall)
                throw new InvalidOperationException("provider failure");
            return Result;
        }
    }
}
