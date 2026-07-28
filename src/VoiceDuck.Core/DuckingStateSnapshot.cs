using System.Collections.ObjectModel;

namespace VoiceDuck.Core;

public sealed class DuckingApplicationStateSnapshot : IEquatable<DuckingApplicationStateSnapshot>
{
    private readonly ReadOnlyCollection<RestorationStatus> _restorationStatuses;

    public ApplicationAudioIdentity Identity { get; }
    public float BaselineVolume { get; }
    public bool IsDucked { get; }
    public IReadOnlyList<RestorationStatus> RestorationStatuses => _restorationStatuses;

    public DuckingApplicationStateSnapshot(
        ApplicationAudioIdentity identity,
        float baselineVolume,
        bool isDucked,
        IEnumerable<RestorationStatus> restorationStatuses)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(restorationStatuses);

        Identity = identity;
        BaselineVolume = baselineVolume;
        IsDucked = isDucked;
        _restorationStatuses = Array.AsReadOnly(
            restorationStatuses.OrderBy(status => status).ToArray());
    }

    public bool Equals(DuckingApplicationStateSnapshot? other)
    {
        return other is not null
            && Identity.Equals(other.Identity)
            && BaselineVolume.Equals(other.BaselineVolume)
            && IsDucked == other.IsDucked
            && _restorationStatuses.SequenceEqual(other._restorationStatuses);
    }

    public override bool Equals(object? obj)
    {
        return obj is DuckingApplicationStateSnapshot other && Equals(other);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Identity);
        hash.Add(BaselineVolume);
        hash.Add(IsDucked);
        foreach (var status in _restorationStatuses)
            hash.Add(status);
        return hash.ToHashCode();
    }
}

public sealed class DuckingStateSnapshot : IEquatable<DuckingStateSnapshot>
{
    private readonly ReadOnlyCollection<string> _activeTriggers;
    private readonly ReadOnlyCollection<DuckingApplicationStateSnapshot> _applications;

    public DuckingPhase Phase { get; }
    public string? SelectedEndpointId { get; }
    public IReadOnlyList<string> ActiveTriggers => _activeTriggers;
    public IReadOnlyList<DuckingApplicationStateSnapshot> Applications => _applications;

    public DuckingStateSnapshot(
        DuckingPhase phase,
        string? selectedEndpointId,
        IEnumerable<string> activeTriggers,
        IEnumerable<DuckingApplicationStateSnapshot> applications)
    {
        ArgumentNullException.ThrowIfNull(activeTriggers);
        ArgumentNullException.ThrowIfNull(applications);

        Phase = phase;
        SelectedEndpointId = selectedEndpointId;
        _activeTriggers = Array.AsReadOnly(activeTriggers
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(trigger => trigger, StringComparer.OrdinalIgnoreCase)
            .ToArray());
        _applications = Array.AsReadOnly(applications
            .OrderBy(application => application.Identity.RenderDeviceId, StringComparer.Ordinal)
            .ThenBy(application => application.Identity.ExecutablePath, StringComparer.OrdinalIgnoreCase)
            .ToArray());
    }

    public bool Equals(DuckingStateSnapshot? other)
    {
        return other is not null
            && Phase == other.Phase
            && string.Equals(
                SelectedEndpointId,
                other.SelectedEndpointId,
                StringComparison.Ordinal)
            && _activeTriggers.SequenceEqual(other._activeTriggers, StringComparer.OrdinalIgnoreCase)
            && _applications.SequenceEqual(other._applications);
    }

    public override bool Equals(object? obj)
    {
        return obj is DuckingStateSnapshot other && Equals(other);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Phase);
        hash.Add(SelectedEndpointId, StringComparer.Ordinal);
        foreach (var trigger in _activeTriggers)
            hash.Add(trigger, StringComparer.OrdinalIgnoreCase);
        foreach (var application in _applications)
            hash.Add(application);
        return hash.ToHashCode();
    }
}

public sealed class DuckingStateSnapshotChangeDetector
{
    private DuckingStateSnapshot? _previous;

    public bool ShouldLog(DuckingStateSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (_previous is not null && _previous.Equals(snapshot))
            return false;

        _previous = snapshot;
        return true;
    }
}
