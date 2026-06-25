namespace VoiceDuck.Core;

public class TriggerApp : IEquatable<TriggerApp>
{
    public string ProcessName { get; }
    public string DisplayName { get; }
    public bool Enabled { get; init; } = true;

    public TriggerApp(string processName)
        : this(processName, processName)
    {
    }

    public TriggerApp(string processName, string displayName)
    {
        if (string.IsNullOrWhiteSpace(processName))
            throw new ArgumentException("Process name must not be empty", nameof(processName));
        ProcessName = processName;
        DisplayName = displayName;
    }

    public bool Equals(TriggerApp? other)
    {
        if (other is null) return false;
        return StringComparer.OrdinalIgnoreCase.Equals(ProcessName, other.ProcessName);
    }

    public override bool Equals(object? obj) => obj is TriggerApp other && Equals(other);

    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(ProcessName);

    public static bool operator ==(TriggerApp? left, TriggerApp? right) =>
        EqualityComparer<TriggerApp>.Default.Equals(left, right);

    public static bool operator !=(TriggerApp? left, TriggerApp? right) =>
        !(left == right);
}
