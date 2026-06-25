namespace VoiceDuck.Core;

public record AudioSessionInfo(
    AudioSessionIdentity Identity,
    float Volume,
    bool IsMuted)
{
    public override string ToString() =>
        $"{Identity} vol={Volume:F2} mute={IsMuted}";
}
