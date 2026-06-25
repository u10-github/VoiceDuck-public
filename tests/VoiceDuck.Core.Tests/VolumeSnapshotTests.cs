namespace VoiceDuck.Core.Tests;

public class VolumeSnapshotTests
{
    private const string DefaultDevice = "default-device";

    [Fact]
    public void Create_with_identity_and_volume()
    {
        var identity = new AudioSessionIdentity(1234, "Discord.exe", DefaultDevice, "inst-abc");
        var snapshot = new VolumeSnapshot(identity, 0.8f);
        Assert.Equal(identity, snapshot.SessionIdentity);
        Assert.Equal(0.8f, snapshot.OriginalVolume);
    }

    [Fact]
    public void Equality_by_identity()
    {
        var id = new AudioSessionIdentity(1234, "Discord.exe", DefaultDevice, "inst-1");
        var a = new VolumeSnapshot(id, 0.8f);
        var b = new VolumeSnapshot(id, 0.5f);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Inequality_by_different_identity()
    {
        var a = new VolumeSnapshot(new AudioSessionIdentity(1234, "A.exe", DefaultDevice, "inst-1"), 0.8f);
        var b = new VolumeSnapshot(new AudioSessionIdentity(5678, "B.exe", DefaultDevice, "inst-2"), 0.8f);
        Assert.NotEqual(a, b);
    }
}
