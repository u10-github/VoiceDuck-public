namespace VoiceDuck.Core;

public sealed class ApplicationVolumeStateStore
{
    private readonly Dictionary<ApplicationAudioIdentity, ApplicationVolumeState> _states = new();

    public int Count => _states.Count;

    public void Add(ApplicationVolumeState state)
    {
        _states[state.Identity] = state;
    }

    public bool TryGet(ApplicationAudioIdentity identity, out ApplicationVolumeState? state)
    {
        return _states.TryGetValue(identity, out state);
    }

    public void Remove(ApplicationAudioIdentity identity)
    {
        _states.Remove(identity);
    }

    public IReadOnlyList<ApplicationVolumeState> GetAll()
    {
        return _states.Values.ToList();
    }

    public void Clear()
    {
        _states.Clear();
    }
}
