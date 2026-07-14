namespace VoiceDuck.Core.Tests;

public class ApplicationVolumeStateStoreTests
{
    private static readonly ApplicationAudioIdentity DevA = new("device-A", @"C:\App\a.exe");
    private static readonly ApplicationAudioIdentity DevB = new("device-B", @"C:\App\a.exe");
    private static readonly ApplicationAudioIdentity OtherPath = new("device-A", @"C:\App\b.exe");

    private static ApplicationVolumeState State(ApplicationAudioIdentity id, float vol, bool ducked) =>
        new(id, vol, ducked);

    [Fact]
    public void Add_and_TryGet()
    {
        var store = new ApplicationVolumeStateStore();
        store.Add(State(DevA, 0.8f, true));
        Assert.True(store.TryGet(DevA, out var state));
        Assert.Equal(0.8f, state!.BaselineVolume);
    }

    [Fact]
    public void Add_overwrites()
    {
        var store = new ApplicationVolumeStateStore();
        store.Add(State(DevA, 0.8f, true));
        store.Add(State(DevA, 0.5f, false));
        Assert.True(store.TryGet(DevA, out var state));
        Assert.Equal(0.5f, state!.BaselineVolume);
        Assert.False(state.IsDucked);
    }

    [Fact]
    public void Remove_removes_entry()
    {
        var store = new ApplicationVolumeStateStore();
        store.Add(State(DevA, 0.8f, true));
        store.Remove(DevA);
        Assert.False(store.TryGet(DevA, out _));
    }

    [Fact]
    public void Different_devices_are_independent()
    {
        var store = new ApplicationVolumeStateStore();
        store.Add(State(DevA, 0.8f, true));
        store.Add(State(DevB, 0.5f, false));
        Assert.Equal(2, store.Count);
        Assert.True(store.TryGet(DevA, out var a));
        Assert.Equal(0.8f, a!.BaselineVolume);
        Assert.True(store.TryGet(DevB, out var b));
        Assert.Equal(0.5f, b!.BaselineVolume);
    }

    [Fact]
    public void Same_device_different_paths_are_independent()
    {
        var store = new ApplicationVolumeStateStore();
        store.Add(State(DevA, 0.8f, true));
        store.Add(State(OtherPath, 0.5f, false));
        Assert.Equal(2, store.Count);
    }

    [Fact]
    public void GetAll_returns_all()
    {
        var store = new ApplicationVolumeStateStore();
        store.Add(State(DevA, 0.8f, true));
        store.Add(State(DevB, 0.5f, false));
        Assert.Equal(2, store.GetAll().Count);
    }

    [Fact]
    public void Clear_removes_all()
    {
        var store = new ApplicationVolumeStateStore();
        store.Add(State(DevA, 0.8f, true));
        store.Clear();
        Assert.Empty(store.GetAll());
    }

    [Fact]
    public void Count_tracks_additions()
    {
        var store = new ApplicationVolumeStateStore();
        Assert.Equal(0, store.Count);
        store.Add(State(DevA, 0.8f, true));
        Assert.Equal(1, store.Count);
        store.Remove(DevA);
        Assert.Equal(0, store.Count);
    }
}
