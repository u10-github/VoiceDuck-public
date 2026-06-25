namespace VoiceDuck.Core.Tests;

public class VolumeSnapshotStoreTests
{
    private const string DevA = "device-A";
    private const string DevB = "device-B";

    private static AudioSessionIdentity Id(uint pid, string name, string device, string inst) =>
        new(pid, name, device, inst);

    private static VolumeSnapshot Snap(uint pid, string name, float vol, string device, string inst) =>
        new(Id(pid, name, device, inst), vol);

    [Fact]
    public void Add_and_get_snapshot_by_identity()
    {
        var store = new VolumeSnapshotStore();
        var snapshot = Snap(1234, "Discord.exe", 0.8f, DevA, "inst-1");
        store.Add(snapshot);
        Assert.True(store.TryGet(snapshot.SessionIdentity, out var got));
        Assert.Equal(snapshot, got);
    }

    [Fact]
    public void Add_duplicate_identity_overwrites()
    {
        var store = new VolumeSnapshotStore();
        var id = Id(1234, "Discord.exe", DevA, "inst-1");
        store.Add(new VolumeSnapshot(id, 0.8f));
        store.Add(new VolumeSnapshot(id, 0.5f));
        Assert.True(store.TryGet(id, out var got));
        Assert.Equal(0.5f, got!.OriginalVolume);
    }

    [Fact]
    public void Same_pid_different_device_sessions_are_independent()
    {
        var store = new VolumeSnapshotStore();
        store.Add(Snap(100, "Chrome.exe", 0.8f, DevA, "inst-1"));
        store.Add(Snap(100, "Chrome.exe", 0.5f, DevB, "inst-2"));
        Assert.Equal(2, store.Count);
        Assert.True(store.TryGet(Id(100, "Chrome.exe", DevA, "inst-1"), out var snapA));
        Assert.Equal(0.8f, snapA!.OriginalVolume);
        Assert.True(store.TryGet(Id(100, "Chrome.exe", DevB, "inst-2"), out var snapB));
        Assert.Equal(0.5f, snapB!.OriginalVolume);
    }

    [Fact]
    public void Contains_returns_true_for_added()
    {
        var store = new VolumeSnapshotStore();
        store.Add(Snap(1234, "Discord.exe", 0.8f, DevA, "inst-1"));
        Assert.True(store.Contains(Id(1234, "Discord.exe", DevA, "inst-1")));
    }

    [Fact]
    public void Contains_returns_false_for_missing()
    {
        var store = new VolumeSnapshotStore();
        Assert.False(store.Contains(Id(9999, "unknown.exe", DevA, "inst-x")));
    }

    [Fact]
    public void Remove_removes_snapshot()
    {
        var store = new VolumeSnapshotStore();
        store.Add(Snap(1234, "Discord.exe", 0.8f, DevA, "inst-1"));
        store.Remove(Id(1234, "Discord.exe", DevA, "inst-1"));
        Assert.False(store.Contains(Id(1234, "Discord.exe", DevA, "inst-1")));
    }

    [Fact]
    public void Remove_missing_does_not_throw()
    {
        var store = new VolumeSnapshotStore();
        store.Remove(Id(9999, "missing.exe", DevA, "inst-x"));
    }

    [Fact]
    public void GetAll_returns_all_snapshots()
    {
        var store = new VolumeSnapshotStore();
        store.Add(Snap(1, "A.exe", 0.5f, DevA, "inst-1"));
        store.Add(Snap(2, "B.exe", 0.6f, DevB, "inst-2"));
        var all = store.GetAll();
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public void GetAll_returns_empty_when_empty()
    {
        var store = new VolumeSnapshotStore();
        Assert.Empty(store.GetAll());
    }

    [Fact]
    public void Clear_removes_all()
    {
        var store = new VolumeSnapshotStore();
        store.Add(Snap(1, "A.exe", 0.5f, DevA, "inst-1"));
        store.Add(Snap(2, "B.exe", 0.6f, DevB, "inst-2"));
        store.Clear();
        Assert.Empty(store.GetAll());
    }

    [Fact]
    public void Count_tracks_additions()
    {
        var store = new VolumeSnapshotStore();
        Assert.Equal(0, store.Count);
        store.Add(Snap(1, "A.exe", 0.5f, DevA, "inst-1"));
        Assert.Equal(1, store.Count);
        store.Add(Snap(2, "B.exe", 0.6f, DevB, "inst-2"));
        Assert.Equal(2, store.Count);
        store.Remove(Id(1, "A.exe", DevA, "inst-1"));
        Assert.Equal(1, store.Count);
    }
}
