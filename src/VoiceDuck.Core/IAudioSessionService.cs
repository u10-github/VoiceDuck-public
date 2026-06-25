namespace VoiceDuck.Core;

public interface IAudioSessionService
{
    IReadOnlyList<AudioSessionInfo> GetAllSessions();
}
