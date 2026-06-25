namespace VoiceDuck.Core;

public class AudioSessionIdentity : IEquatable<AudioSessionIdentity>
{
    public uint ProcessId { get; }
    public string ProcessName { get; }
    public string RenderDeviceId { get; }
    public string SessionInstanceIdentifier { get; }

    public bool IsResolved =>
        !string.IsNullOrEmpty(RenderDeviceId) && !string.IsNullOrEmpty(SessionInstanceIdentifier);

    public AudioSessionIdentity(uint processId, string processName, string renderDeviceId, string sessionInstanceIdentifier)
    {
        ProcessId = processId;
        ProcessName = processName ?? throw new ArgumentNullException(nameof(processName));
        RenderDeviceId = renderDeviceId ?? throw new ArgumentNullException(nameof(renderDeviceId));
        SessionInstanceIdentifier = sessionInstanceIdentifier ?? throw new ArgumentNullException(nameof(sessionInstanceIdentifier));
    }

    public bool Equals(AudioSessionIdentity? other)
    {
        if (other is null) return false;
        return string.Equals(RenderDeviceId, other.RenderDeviceId, StringComparison.Ordinal)
            && string.Equals(SessionInstanceIdentifier, other.SessionInstanceIdentifier, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj) => obj is AudioSessionIdentity other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(RenderDeviceId, SessionInstanceIdentifier);

    public static bool operator ==(AudioSessionIdentity? left, AudioSessionIdentity? right) =>
        EqualityComparer<AudioSessionIdentity>.Default.Equals(left, right);

    public static bool operator !=(AudioSessionIdentity? left, AudioSessionIdentity? right) =>
        !(left == right);

    public override string ToString() =>
        $"Session(pid={ProcessId}, name={ProcessName}, device={RenderDeviceId}, inst={SessionInstanceIdentifier})";
}
