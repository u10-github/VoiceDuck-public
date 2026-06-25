namespace VoiceDuck.Core;

public interface IMicrophoneStateService
{
    IReadOnlySet<string> GetActiveMicProcessNames();
}
