namespace VoiceDuck.Core;

public class VolumeSnapshotStore
{
    private readonly Dictionary<AudioSessionIdentity, VolumeSnapshot> _snapshots = new();

    public int Count => _snapshots.Count;

    public void Add(VolumeSnapshot snapshot)
    {
        _snapshots[snapshot.SessionIdentity] = snapshot;
    }

    public bool Contains(AudioSessionIdentity identity) => _snapshots.ContainsKey(identity);

    public bool TryGet(AudioSessionIdentity identity, out VolumeSnapshot? snapshot)
    {
        return _snapshots.TryGetValue(identity, out snapshot);
    }

    public void Remove(AudioSessionIdentity identity)
    {
        _snapshots.Remove(identity);
    }

    public IReadOnlyList<VolumeSnapshot> GetAll()
    {
        return _snapshots.Values.ToList();
    }

    public void Clear()
    {
        _snapshots.Clear();
    }
}
