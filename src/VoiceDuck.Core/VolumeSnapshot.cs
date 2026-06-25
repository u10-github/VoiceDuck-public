namespace VoiceDuck.Core;

public class VolumeSnapshot : IEquatable<VolumeSnapshot>
{
    public AudioSessionIdentity SessionIdentity { get; }
    public float OriginalVolume { get; }

    public VolumeSnapshot(AudioSessionIdentity sessionIdentity, float originalVolume)
    {
        SessionIdentity = sessionIdentity ?? throw new ArgumentNullException(nameof(sessionIdentity));
        OriginalVolume = originalVolume;
    }

    public bool Equals(VolumeSnapshot? other)
    {
        if (other is null) return false;
        return EqualityComparer<AudioSessionIdentity>.Default.Equals(SessionIdentity, other.SessionIdentity);
    }

    public override bool Equals(object? obj) => obj is VolumeSnapshot other && Equals(other);

    public override int GetHashCode() => SessionIdentity.GetHashCode();

    public static bool operator ==(VolumeSnapshot? left, VolumeSnapshot? right) =>
        EqualityComparer<VolumeSnapshot>.Default.Equals(left, right);

    public static bool operator !=(VolumeSnapshot? left, VolumeSnapshot? right) =>
        !(left == right);
}
