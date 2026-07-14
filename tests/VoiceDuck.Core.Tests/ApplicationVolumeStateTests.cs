namespace VoiceDuck.Core.Tests;

public class ApplicationVolumeStateTests
{
    private static readonly ApplicationAudioIdentity DefaultId = new("device-A", @"C:\App\app.exe");

    [Fact]
    public void Constructor_sets_properties()
    {
        var state = new ApplicationVolumeState(DefaultId, 0.8f, true);
        Assert.Equal(DefaultId, state.Identity);
        Assert.Equal(0.8f, state.BaselineVolume);
        Assert.True(state.IsDucked);
    }

    [Fact]
    public void SetDucked_changes_flag()
    {
        var state = new ApplicationVolumeState(DefaultId, 0.8f, true);
        state.SetDucked(false);
        Assert.False(state.IsDucked);
    }

    [Fact]
    public void Null_identity_throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ApplicationVolumeState(null!, 0.8f, true));
    }
}
