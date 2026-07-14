namespace VoiceDuck.Core.Tests;

public class ApplicationAudioIdentityTests
{
    private const string DeviceA = "device-A";
    private const string DeviceB = "device-B";
    private const string Path1 = @"C:\Program Files\App\app.exe";
    private const string Path2 = @"D:\Tools\tool.exe";

    [Fact]
    public void Same_device_and_path_are_equal()
    {
        var a = new ApplicationAudioIdentity(DeviceA, Path1);
        var b = new ApplicationAudioIdentity(DeviceA, Path1);
        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Same_device_case_insensitive_path_are_equal()
    {
        var a = new ApplicationAudioIdentity(DeviceA, @"C:\Program Files\App\app.exe");
        var b = new ApplicationAudioIdentity(DeviceA, @"c:\program files\app\app.exe");
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Different_device_not_equal()
    {
        var a = new ApplicationAudioIdentity(DeviceA, Path1);
        var b = new ApplicationAudioIdentity(DeviceB, Path1);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Different_path_not_equal()
    {
        var a = new ApplicationAudioIdentity(DeviceA, Path1);
        var b = new ApplicationAudioIdentity(DeviceA, Path2);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void IsResolved_true_when_both_device_and_path_present()
    {
        var id = new ApplicationAudioIdentity(DeviceA, Path1);
        Assert.True(id.IsResolved);
    }

    [Fact]
    public void IsResolved_false_when_device_empty()
    {
        var id = new ApplicationAudioIdentity("", Path1);
        Assert.False(id.IsResolved);
    }

    [Fact]
    public void IsResolved_false_when_path_empty()
    {
        var id = new ApplicationAudioIdentity(DeviceA, "");
        Assert.False(id.IsResolved);
    }

    [Fact]
    public void IsResolved_false_when_both_empty()
    {
        var id = new ApplicationAudioIdentity("", "");
        Assert.False(id.IsResolved);
    }

    [Fact]
    public void ToString_contains_device_and_path()
    {
        var id = new ApplicationAudioIdentity(DeviceA, Path1);
        var str = id.ToString();
        Assert.Contains(DeviceA, str);
        Assert.Contains(Path1, str);
    }

    [Fact]
    public void Null_device_throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ApplicationAudioIdentity(null!, Path1));
    }

    [Fact]
    public void Null_path_throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ApplicationAudioIdentity(DeviceA, null!));
    }
}
