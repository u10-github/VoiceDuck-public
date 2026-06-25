namespace VoiceDuck.Core.Tests;

public class TriggerAppTests
{
    [Fact]
    public void Create_with_process_name()
    {
        var app = new TriggerApp("Discord.exe");
        Assert.Equal("Discord.exe", app.ProcessName);
    }

    [Fact]
    public void Create_with_process_name_and_display_name()
    {
        var app = new TriggerApp("Discord.exe", "Discord");
        Assert.Equal("Discord.exe", app.ProcessName);
        Assert.Equal("Discord", app.DisplayName);
    }

    [Fact]
    public void Enabled_defaults_to_true()
    {
        var app = new TriggerApp("Discord.exe");
        Assert.True(app.Enabled);
    }

    [Fact]
    public void Equality_by_process_name()
    {
        var a = new TriggerApp("Discord.exe");
        var b = new TriggerApp("Discord.exe");
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Inequality_by_different_process_name()
    {
        var a = new TriggerApp("Discord.exe");
        var b = new TriggerApp("DiscordCanary.exe");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void DisplayName_falls_back_to_process_name()
    {
        var app = new TriggerApp("Discord.exe");
        Assert.Equal("Discord.exe", app.DisplayName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    public void Constructor_throws_on_invalid_process_name(string? invalid)
    {
        Assert.Throws<ArgumentException>(() => new TriggerApp(invalid!));
    }
}
