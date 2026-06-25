namespace VoiceDuck.Core.Tests;

public class AudioSessionIdentityTests
{
    private const string DefaultDevice = "{0.0.0.00000000}.default";
    private const string DefaultSession = "session-instance-1";

    private static AudioSessionIdentity Make(uint pid, string name) =>
        new(pid, name, DefaultDevice, DefaultSession);

    [Fact]
    public void Create_with_process_id_and_name()
    {
        var id = new AudioSessionIdentity(1234, "Discord.exe", DefaultDevice, "inst-abc");
        Assert.Equal(1234u, id.ProcessId);
        Assert.Equal("Discord.exe", id.ProcessName);
        Assert.Equal(DefaultDevice, id.RenderDeviceId);
        Assert.Equal("inst-abc", id.SessionInstanceIdentifier);
    }

    [Fact]
    public void Equality_by_render_device_and_session_instance()
    {
        var a = new AudioSessionIdentity(1234, "Discord.exe", "device-A", "inst-1");
        var b = new AudioSessionIdentity(1234, "Discord.exe", "device-A", "inst-1");
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Inequality_by_different_device()
    {
        var a = new AudioSessionIdentity(1234, "Discord.exe", "device-A", "inst-1");
        var b = new AudioSessionIdentity(1234, "Discord.exe", "device-B", "inst-1");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Inequality_by_different_session_instance()
    {
        var a = new AudioSessionIdentity(1234, "Discord.exe", "device-A", "inst-1");
        var b = new AudioSessionIdentity(1234, "Discord.exe", "device-A", "inst-2");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Same_pid_different_device_are_not_equal()
    {
        var a = new AudioSessionIdentity(100, "Chrome.exe", "device-A", "inst-1");
        var b = new AudioSessionIdentity(100, "Chrome.exe", "device-B", "inst-2");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void IsResolved_true_when_both_fields_have_value()
    {
        var id = new AudioSessionIdentity(100, "Discord.exe", "device-A", "inst-1");
        Assert.True(id.IsResolved);
    }

    [Fact]
    public void IsResolved_false_when_session_instance_empty()
    {
        var id = new AudioSessionIdentity(100, "Discord.exe", "device-A", "");
        Assert.False(id.IsResolved);
    }

    [Fact]
    public void IsResolved_false_when_render_device_empty()
    {
        var id = new AudioSessionIdentity(100, "Discord.exe", "", "inst-1");
        Assert.False(id.IsResolved);
    }

    [Fact]
    public void IsResolved_false_when_both_empty()
    {
        var id = new AudioSessionIdentity(100, "Discord.exe", "", "");
        Assert.False(id.IsResolved);
    }

    [Fact]
    public void ToString_contains_identity_fields()
    {
        var id = new AudioSessionIdentity(1234, "Discord.exe", DefaultDevice, "inst-abc");
        var str = id.ToString();
        Assert.Contains("1234", str);
        Assert.Contains("Discord.exe", str);
        Assert.Contains(DefaultDevice, str);
        Assert.Contains("inst-abc", str);
    }
}
