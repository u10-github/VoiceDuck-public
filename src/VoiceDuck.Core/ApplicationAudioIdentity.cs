namespace VoiceDuck.Core;

public class ApplicationAudioIdentity : IEquatable<ApplicationAudioIdentity>
{
    public string RenderDeviceId { get; }
    public string ExecutablePath { get; }
    public bool IsResolved =>
        !string.IsNullOrEmpty(RenderDeviceId) && !string.IsNullOrEmpty(ExecutablePath);

    public ApplicationAudioIdentity(string renderDeviceId, string executablePath)
    {
        RenderDeviceId = renderDeviceId ?? throw new ArgumentNullException(nameof(renderDeviceId));
        ExecutablePath = executablePath ?? throw new ArgumentNullException(nameof(executablePath));
    }

    public bool Equals(ApplicationAudioIdentity? other)
    {
        if (other is null) return false;
        return string.Equals(RenderDeviceId, other.RenderDeviceId, StringComparison.Ordinal)
            && string.Equals(ExecutablePath, other.ExecutablePath, StringComparison.OrdinalIgnoreCase);
    }

    public override bool Equals(object? obj) => obj is ApplicationAudioIdentity other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(RenderDeviceId, ExecutablePath.ToLowerInvariant());

    public static bool operator ==(ApplicationAudioIdentity? left, ApplicationAudioIdentity? right) =>
        EqualityComparer<ApplicationAudioIdentity>.Default.Equals(left, right);

    public static bool operator !=(ApplicationAudioIdentity? left, ApplicationAudioIdentity? right) =>
        !(left == right);

    public override string ToString() =>
        $"AppIdentity(device={RenderDeviceId}, path={ExecutablePath})";
}
