using VoiceDuck.Core;
using VoiceDuck.Extensions.WindowsAudio;
using VoiceDuck.Infrastructure;

namespace VoiceDuck.App.Console;

public static class DuckingServiceFactory
{
    public static VolumeDuckingService Create(
        IAudioSessionVolumeWriter volumeWriter,
        DuckingSessionClassifier classifier,
        ApplicationVolumeStateStore stateStore,
        IRestorationObligationRepository obligationRepo,
        IAudioEndpointSelector endpointSelector,
        TextWriter? errorWriter = null)
    {
        var logger = errorWriter != null ? new ConsoleLogger(errorWriter) : new ConsoleLogger();
        return new VolumeDuckingService(
            volumeWriter,
            classifier,
            stateStore,
            obligationRepo,
            endpointSelector,
            logger: logger);
    }
}
