using VoiceDuck.Core;
using VoiceDuck.Extensions.WindowsAudio;
using VoiceDuck.Infrastructure;

namespace VoiceDuck.App.Wpf;

public sealed class DuckingOrchestrator : IDisposable
{
    public event Action<DuckingPhase, IReadOnlySet<string>>? PhaseChanged;
    public VoiceDuckSettings Settings => _settings;
    public string SettingsPath => _settingsPath;
    public bool IsManualDucking { get; private set; }

    private readonly string _settingsPath;
    private readonly SimpleLogger _logger;
    private readonly object _gate = new();
    private DuckingStateMachine _stateMachine = null!;
    private VolumeSnapshotStore _snapshotStore = null!;
    private VolumeDuckingService _duckingService = null!;
    private WindowsAudioSessionService _sessionService = null!;
    private WindowsMicrophoneStateService _micService = null!;
    private VoiceDuckSettings _settings = null!;
    private string _currentProcessName = null!;
    private System.Threading.Timer? _timer;
    private DateTimeOffset? _restoreDueAt;
    private int _isPolling;
    private bool _disposed;

    public DuckingOrchestrator(string settingsPath, SimpleLogger logger)
    {
        _settingsPath = settingsPath;
        _logger = logger;
    }

    public void Start()
    {
        var repository = new SettingsRepository(_settingsPath);
        _settings = repository.LoadOrDefault();

        _currentProcessName = System.Diagnostics.Process.GetCurrentProcess().ProcessName;
        if (!_currentProcessName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            _currentProcessName += ".exe";

        _stateMachine = new DuckingStateMachine();
        var classifier = new DuckingSessionClassifier();
        _snapshotStore = new VolumeSnapshotStore();
        var volumeWriter = new WindowsAudioSessionVolumeWriter();
        _duckingService = new VolumeDuckingService(volumeWriter, classifier, _snapshotStore);
        _sessionService = new WindowsAudioSessionService();
        _micService = new WindowsMicrophoneStateService();

        _logger.Info($"VoiceDuck started. Settings: {_settingsPath}");
        _logger.Info($"  Ducking ratio: {_settings.Policy.DuckingRatio:F2}, restore delay: {_settings.Policy.RestoreDelaySeconds}s");
        _logger.Info($"  Trigger apps: {string.Join(", ", _settings.TriggerApps.Where(t => t.Enabled).Select(t => t.ProcessName))}");

        NotifyPhaseChanged();

        _timer = new System.Threading.Timer(Poll, null, TimeSpan.Zero, TimeSpan.FromSeconds(3));
    }

    public void ManualDuckOn()
    {
        if (IsManualDucking || _disposed)
            return;

        lock (_gate)
        {
            IsManualDucking = true;
            var sessions = _sessionService.GetAllSessions();
            _duckingService.ApplyDucking(sessions, _settings, _currentProcessName);
            _logger.Info($"Manual duck ON ({_snapshotStore.Count} session(s))");
        }

        NotifyPhaseChanged();
    }

    public void ManualDuckOff()
    {
        if (!IsManualDucking || _disposed)
            return;

        lock (_gate)
        {
            IsManualDucking = false;

            try
            {
                _logger.Info($"Manual duck OFF restore: {_snapshotStore.Count} session(s)");
                LogSnapshotsBeforeRestore();
                _duckingService.RestoreVolumes();
                _logger.Info("Manual duck OFF restore completed");
            }
            catch (Exception ex)
            {
                _logger.Error("Manual duck OFF restore error", ex);
            }

            _stateMachine = new DuckingStateMachine();
            _restoreDueAt = null;
            _logger.Info("Manual duck OFF, auto detection resumed");
        }

        NotifyPhaseChanged();
    }

    public void ReloadSettings()
    {
        var repository = new SettingsRepository(_settingsPath);
        var newSettings = repository.LoadOrDefault();
        lock (_gate)
        {
            _settings = newSettings;
        }
        _logger.Info("Settings reloaded");
        NotifyPhaseChanged();
    }

    public void RestoreAndStop()
    {
        _disposed = true;
        _timer?.Dispose();
        _timer = null;

        lock (_gate)
        {
            if (_snapshotStore is { Count: > 0 })
            {
                _logger.Info($"Shutdown restore: {_snapshotStore.Count} session(s)");
                LogSnapshotsBeforeRestore();

                try
                {
                    _duckingService.RestoreVolumes();
                    _logger.Info("Shutdown restore completed");
                }
                catch (Exception ex)
                {
                    _logger.Error($"Shutdown restore error", ex);
                }
            }
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            RestoreAndStop();
        }
    }

    private void Poll(object? state)
    {
        if (_disposed)
            return;

        if (Interlocked.Exchange(ref _isPolling, 1) == 1)
            return;

        DuckingPhase? phaseToNotify = null;
        IReadOnlySet<string>? triggersToNotify = null;

        try
        {
            lock (_gate)
            {
                if (_disposed)
                    return;

                if (IsManualDucking)
                    return;

                var activeMicProcesses = _micService.GetActiveMicProcessNames();
                var activeTriggerNames = _settings.TriggerApps
                    .Where(t => t.Enabled && activeMicProcesses.Contains(t.ProcessName))
                    .Select(t => t.ProcessName)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var previouslyActive = _stateMachine.ActiveTriggerApps
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (var name in activeTriggerNames)
                {
                    if (!previouslyActive.Contains(name))
                        _stateMachine.NotifyTriggerAppActive(name);
                }
                foreach (var name in previouslyActive)
                {
                    if (!activeTriggerNames.Contains(name))
                        _stateMachine.NotifyTriggerAppInactive(name);
                }

                switch (_stateMachine.Phase)
                {
                    case DuckingPhase.Ducking:
                        {
                            _restoreDueAt = null;
                            var sessions = _sessionService.GetAllSessions();
                            _duckingService.ApplyDucking(sessions, _settings, _currentProcessName);
                            _logger.Info($"Ducking: {_snapshotStore.Count} session(s), triggers={string.Join(",", activeTriggerNames)}");
                            LogSnapshotsAfterDucking();
                            break;
                        }
                    case DuckingPhase.WaitingForRestore:
                        {
                            if (_restoreDueAt == null)
                            {
                                _restoreDueAt = DateTimeOffset.Now.AddSeconds(_settings.Policy.RestoreDelaySeconds);
                                _logger.Info($"All triggers inactive, restore due at {_restoreDueAt.Value:HH:mm:ss}");
                            }
                            else if (DateTimeOffset.Now >= _restoreDueAt.Value)
                            {
                                _stateMachine.NotifyRestoreDelayElapsed();
                                _restoreDueAt = null;
                                _logger.Info("Restore delay elapsed, transitioning to Restoring");
                            }
                            break;
                        }
                    case DuckingPhase.Restoring:
                        {
                            var count = _snapshotStore.Count;
                            _logger.Info($"Restoring: {count} session(s)");
                            LogSnapshotsBeforeRestore();

                            var restored = false;

                            try
                            {
                                _duckingService.RestoreVolumes();
                                _logger.Info("Restore completed");
                                restored = true;
                            }
                            catch (Exception ex)
                            {
                                _logger.Error("Restore error", ex);
                            }

                            if (restored)
                            {
                                _stateMachine.NotifyRestoreCompleted();
                                _restoreDueAt = null;
                            }

                            break;
                        }
                    case DuckingPhase.Idle:
                        {
                            _restoreDueAt = null;
                            break;
                        }
                }

                phaseToNotify = _stateMachine.Phase;
                triggersToNotify = _stateMachine.ActiveTriggerApps
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
            }

            if (phaseToNotify.HasValue)
                PhaseChanged?.Invoke(phaseToNotify.Value, triggersToNotify!);
        }
        catch (Exception ex)
        {
            _logger.Error("Poll error", ex);
        }
        finally
        {
            Interlocked.Exchange(ref _isPolling, 0);
        }
    }

    private void NotifyPhaseChanged()
    {
        var triggers = _stateMachine.ActiveTriggerApps
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        PhaseChanged?.Invoke(_stateMachine.Phase, triggers);
    }

    private void LogSnapshotsBeforeRestore()
    {
        foreach (var snap in _snapshotStore.GetAll())
        {
            var id = snap.SessionIdentity;
            _logger.Info($"  {id.ProcessName}(pid={id.ProcessId}) dev={id.RenderDeviceId} inst={id.SessionInstanceIdentifier}: restore to {snap.OriginalVolume:F2}");
        }
    }

    private void LogSnapshotsAfterDucking()
    {
        foreach (var snap in _snapshotStore.GetAll())
        {
            var id = snap.SessionIdentity;
            var ducked = _settings.Policy.ComputeDuckedVolume(snap.OriginalVolume);
            _logger.Info($"  {id.ProcessName}(pid={id.ProcessId}) dev={id.RenderDeviceId} inst={id.SessionInstanceIdentifier}: {snap.OriginalVolume:F2} -> {ducked:F2}");
        }
    }
}
