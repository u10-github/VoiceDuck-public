namespace VoiceDuck.Core;

public record AudioSessionInfo(
    AudioSessionIdentity Identity,
    float Volume,
    bool IsMuted,
    string? ExecutablePath = null)
{
    public override string ToString() =>
        $"{Identity} vol={Volume:F2} mute={IsMuted} path={ExecutablePath ?? "(unknown)"}";
}
