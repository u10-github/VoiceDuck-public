namespace VoiceDuck.Core;

public class DuckingStateMachine
{
    private readonly HashSet<string> _activeTriggerApps = new(StringComparer.OrdinalIgnoreCase);
    private DuckingPhase _phase = DuckingPhase.Idle;

    public DuckingPhase Phase => _phase;
    public IReadOnlySet<string> ActiveTriggerApps => _activeTriggerApps;
    public bool IsDucking => _phase is DuckingPhase.Ducking or DuckingPhase.WaitingForRestore;

    public DuckingPhase NotifyTriggerAppActive(string processName)
    {
        ValidateProcessName(processName);
        _activeTriggerApps.Add(processName);
        if (_phase is DuckingPhase.Idle or DuckingPhase.WaitingForRestore or DuckingPhase.Restoring)
        {
            _phase = DuckingPhase.Ducking;
        }
        return _phase;
    }

    public DuckingPhase NotifyTriggerAppInactive(string processName)
    {
        ValidateProcessName(processName);
        _activeTriggerApps.Remove(processName);
        if (_phase == DuckingPhase.Ducking && _activeTriggerApps.Count == 0)
        {
            _phase = DuckingPhase.WaitingForRestore;
        }
        return _phase;
    }

    public DuckingPhase NotifyRestoreDelayElapsed()
    {
        if (_phase == DuckingPhase.WaitingForRestore)
        {
            _phase = DuckingPhase.Restoring;
        }
        return _phase;
    }

    public DuckingPhase NotifyRestoreCompleted()
    {
        if (_phase == DuckingPhase.Restoring)
        {
            _phase = DuckingPhase.Idle;
            _activeTriggerApps.Clear();
        }
        return _phase;
    }

    private static void ValidateProcessName(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
            throw new ArgumentException("Process name must not be empty", nameof(processName));
    }
}
