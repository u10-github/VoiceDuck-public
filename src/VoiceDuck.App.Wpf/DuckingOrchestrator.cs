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
    private readonly DuckingStateSnapshotChangeDetector _stateSnapshotChangeDetector = new();
    private DuckingStateMachine _stateMachine = null!;
    private ApplicationVolumeStateStore _stateStore = null!;
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
        _stateStore = new ApplicationVolumeStateStore();
        var volumeWriter = new WindowsAudioSessionVolumeWriter();
        var endpointSelector = new WindowsDefaultMultimediaEndpointSelector();
        var obligationRepoPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VoiceDuck",
            "pending-restores.json");
        var obligationRepo = new RestorationObligationRepository(obligationRepoPath);
        _duckingService = new VolumeDuckingService(volumeWriter, classifier, _stateStore, obligationRepo, endpointSelector, new SimpleLoggerAdapter(_logger));
        _sessionService = new WindowsAudioSessionService();
        _micService = new WindowsMicrophoneStateService();

        var recovery = _duckingService.LoadAndPopulateStartupState();
        if (recovery.WasCorrupt)
            _logger.Warn("Startup recovery: obligation store was corrupt, valid records loaded in-memory");
        _logger.Info($"Startup recovery: {recovery.LoadedCount} obligation(s) loaded, saved={recovery.Saved}");

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
            _logger.Info($"Manual duck ON ({_stateStore.Count} app(s))");
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
                var sessions = _sessionService.GetAllSessions();
                _logger.Info($"Manual duck OFF restore: {_stateStore.Count} app(s)");
                LogStateSummary();
                _duckingService.RestoreVolumes(sessions);
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
            if (_stateStore.Count > 0)
            {
                _logger.Info($"Shutdown restore: {_stateStore.Count} app(s)");
                LogStateSummary();

                try
                {
                    var sessions = _sessionService.GetAllSessions();
                    _duckingService.RestoreVolumes(sessions);
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

                var sessions = _sessionService.GetAllSessions();

                switch (_stateMachine.Phase)
                {
                    case DuckingPhase.Ducking:
                        {
                            _restoreDueAt = null;
                            _duckingService.ApplyDucking(sessions, _settings, _currentProcessName);
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
                            try
                            {
                                _duckingService.RestoreVolumes(sessions);

                                if (_stateStore.Count == 0)
                                    _logger.Info("Restore completed");
                                else
                                    _logger.Warn($"Restore partial: {_stateStore.Count} app(s) retained for deferred restore");

                                LogStateSnapshotIfChanged();
                                _stateMachine.NotifyRestoreCompleted();
                                _restoreDueAt = null;
                            }
                            catch (Exception ex)
                            {
                                _logger.Error("Restore error", ex);
                            }

                            break;
                        }
                    case DuckingPhase.Idle:
                        {
                            _restoreDueAt = null;
                            _duckingService.ApplyDeferredRestores(sessions);
                            break;
                        }
                }

                LogStateSnapshotIfChanged();
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

    private void LogStateSummary()
    {
        foreach (var state in _stateStore.GetAll())
        {
            _logger.Info($"  {state.Identity}: baseline={state.BaselineVolume:F2} ducked={state.IsDucked}");
        }
    }

    private void LogStateSnapshotIfChanged()
    {
        var snapshot = _duckingService.CaptureStateSnapshot(
            _stateMachine.Phase,
            _stateMachine.ActiveTriggerApps);
        if (!_stateSnapshotChangeDetector.ShouldLog(snapshot))
            return;

        _logger.Info(
            $"StateSnapshot: phase={snapshot.Phase} endpoint={snapshot.SelectedEndpointId ?? "(none)"} triggers={string.Join(",", snapshot.ActiveTriggers)} tracked={snapshot.Applications.Count}");
        foreach (var application in snapshot.Applications)
        {
            var restoration = application.RestorationStatuses.Count == 0
                ? "none"
                : string.Join(",", application.RestorationStatuses);
            _logger.Info(
                $"  {application.Identity}: baseline={application.BaselineVolume:F2} ducked={application.IsDucked} restoration={restoration}");
        }
    }
}
