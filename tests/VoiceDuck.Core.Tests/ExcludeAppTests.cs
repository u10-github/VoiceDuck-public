namespace VoiceDuck.Core.Tests;

public class ExcludeAppTests
{
    [Fact]
    public void Create_with_process_name()
    {
        var app = new ExcludeApp("MusicPlayer.exe");
        Assert.Equal("MusicPlayer.exe", app.ProcessName);
    }

    [Fact]
    public void Equality_by_process_name()
    {
        var a = new ExcludeApp("MusicPlayer.exe");
        var b = new ExcludeApp("MusicPlayer.exe");
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Inequality_by_different_process_name()
    {
        var a = new ExcludeApp("MusicPlayer.exe");
        var b = new ExcludeApp("Game.exe");
        Assert.NotEqual(a, b);
    }
}
