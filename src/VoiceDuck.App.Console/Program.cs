using VoiceDuck.Core;
using VoiceDuck.Extensions.WindowsAudio;
using VoiceDuck.Infrastructure;

var knownPrefixes = new[] { "--settings=" };
var knownFlags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    "--list", "--duck-once", "--restore", "--watch"
};

var unknownArgs = args
    .Where(a => a.StartsWith("--", StringComparison.OrdinalIgnoreCase))
    .Where(a => !knownFlags.Contains(a))
    .Where(a => !knownPrefixes.Any(p => a.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
    .ToList();

if (unknownArgs.Count > 0)
{
    Console.Error.WriteLine($"Unknown option: {unknownArgs[0]}");
    Console.Error.WriteLine("Usage: VoiceDuck.App.Console [--list] [--duck-once] [--restore] [--settings=<path>]");
    Console.Error.WriteLine("  No arguments:   Continuous monitoring mode (Ctrl+C to exit)");
    Console.Error.WriteLine("  --list:         Show audio sessions and microphone usage");
    Console.Error.WriteLine("  --duck-once:    Apply ducking once");
    Console.Error.WriteLine("  --restore:      Restore volumes from saved snapshots");
    Console.Error.WriteLine("  --settings=<p>: Settings file path (default: %%APPDATA%%/VoiceDuck/settings.json)");
    return;
}

var listOnly = args.Contains("--list", StringComparer.OrdinalIgnoreCase);
var duckOnce = args.Contains("--duck-once", StringComparer.OrdinalIgnoreCase);
var restore = args.Contains("--restore", StringComparer.OrdinalIgnoreCase);

var settingsPath = args
    .Where(a => a.StartsWith("--settings=", StringComparison.OrdinalIgnoreCase))
    .Select(a => a.Split('=', 2)[1])
    .FirstOrDefault()
    ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "VoiceDuck",
        "settings.json");

var currentProcessName = System.Diagnostics.Process.GetCurrentProcess().ProcessName;
if (!currentProcessName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
    currentProcessName += ".exe";

if (listOnly)
{
    ShowSessionAndMicInfo();
    return;
}

if (duckOnce || restore)
{
    RunOneShot(duckOnce, restore);
    return;
}

RunContinuousMonitoring();
return;

void ShowSessionAndMicInfo()
{
    Console.WriteLine("=== Audio Sessions (Output) ===");
    try
    {
        var sessionService = new WindowsAudioSessionService();
        var sessions = sessionService.GetAllSessions();
        Console.WriteLine($"Found {sessions.Count} session(s).");
        foreach (var session in sessions)
            Console.WriteLine($"  {session}");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"  Error: {ex.Message}");
    }

    Console.WriteLine();
    Console.WriteLine("=== Microphone Usage (Input) ===");
    try
    {
        var micService = new WindowsMicrophoneStateService();
        var processes = micService.GetActiveMicProcessNames();
        Console.WriteLine($"Found {processes.Count} process(es) using the microphone.");
        foreach (var name in processes)
            Console.WriteLine($"  {name}");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"  Error: {ex.Message}");
    }
}

void RunOneShot(bool duckOnce, bool restore)
{
    var repository = new SettingsRepository(settingsPath);
    var settings = repository.LoadOrDefault();

    var classifier = new DuckingSessionClassifier();
    var snapshotStore = new VolumeSnapshotStore();
    var volumeWriter = new WindowsAudioSessionVolumeWriter();
    var duckingService = new VolumeDuckingService(volumeWriter, classifier, snapshotStore);

    if (duckOnce)
    {
        Console.WriteLine("=== Duck Once ===");
        Console.WriteLine($"Settings: {settingsPath}");
        Console.WriteLine($"  duckingRatio: {settings.Policy.DuckingRatio:F2}");
        Console.WriteLine($"  triggerApps: {string.Join(", ", settings.TriggerApps.Select(t => t.ProcessName))}");
        Console.WriteLine($"VoiceDuck process: {currentProcessName}");

        Console.WriteLine();
        Console.WriteLine("Expected:");
        Console.WriteLine($"  Duck: non-trigger, non-exclude, non-VoiceDuck sessions");
        Console.WriteLine($"  Protect: Trigger Apps, Exclude Apps, VoiceDuck itself");
        Console.WriteLine($"  Skip: PID 0 sessions");

        Console.WriteLine();
        Console.WriteLine("Applying ducking once...");

        try
        {
            var sessionService = new WindowsAudioSessionService();
            var sessions = sessionService.GetAllSessions();

            duckingService.ApplyDucking(sessions, settings, currentProcessName);

            Console.WriteLine("Ducking applied once.");

            var snapshots = snapshotStore.GetAll();
            if (snapshots.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("Snapshots saved:");
                foreach (var snap in snapshots)
                    Console.WriteLine($"  {snap.SessionIdentity.ProcessName} pid={snap.SessionIdentity.ProcessId} originalVolume={snap.OriginalVolume:F2}");
            }
            else
            {
                Console.WriteLine("No snapshots saved (all sessions were protected or skipped).");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  Error during ducking: {ex.Message}");
        }
    }

    if (restore)
    {
        Console.WriteLine("=== Restore ===");

        var restoreSnapshots = snapshotStore.GetAll();
        if (restoreSnapshots.Count == 0 && !duckOnce)
        {
            Console.WriteLine("No snapshots are available in this process. Use --duck-once --restore in the same command to test restore.");
        }
        else
        {
            Console.WriteLine($"Snapshots to restore: {restoreSnapshots.Count}");

            try
            {
                duckingService.RestoreVolumes();
                Console.WriteLine("Volumes restored to original values.");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"  Error during restore: {ex.Message}");
            }
        }
    }

    if (duckOnce && !restore)
    {
        Console.WriteLine();
        Console.WriteLine("Restore is not implemented yet. Please restore app volumes manually in Windows Volume Mixer.");
    }
}

void RunContinuousMonitoring()
{
    var repository = new SettingsRepository(settingsPath);
    var settings = repository.LoadOrDefault();

    Console.WriteLine("=== VoiceDuck Console MVP ===");
    Console.WriteLine($"Settings file: {settingsPath}");
    Console.WriteLine($"  Ducking ratio: {settings.Policy.DuckingRatio:F2}");
    Console.WriteLine($"  Restore delay: {settings.Policy.RestoreDelaySeconds}s");
    Console.WriteLine($"  Trigger apps: {string.Join(", ", settings.TriggerApps.Where(t => t.Enabled).Select(t => t.ProcessName))}");
    if (settings.ExcludeApps.Count > 0)
        Console.WriteLine($"  Exclude apps: {string.Join(", ", settings.ExcludeApps.Select(e => e.ProcessName))}");
    Console.WriteLine("Press Ctrl+C to exit.");
    Console.WriteLine();

    var stateMachine = new DuckingStateMachine();
    var classifier = new DuckingSessionClassifier();
    var snapshotStore = new VolumeSnapshotStore();
    var volumeWriter = new WindowsAudioSessionVolumeWriter();
    var duckingService = new VolumeDuckingService(volumeWriter, classifier, snapshotStore);
    var sessionService = new WindowsAudioSessionService();
    var micService = new WindowsMicrophoneStateService();
    DateTimeOffset? restoreDueAt = null;
    var ctrlCPressed = false;

    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        ctrlCPressed = true;
    };

    while (!ctrlCPressed)
    {
        Console.WriteLine($"--- Poll at {DateTime.Now:HH:mm:ss} ---");

        try
        {
            var activeMicProcesses = micService.GetActiveMicProcessNames();
            var activeTriggerNames = settings.TriggerApps
                .Where(t => t.Enabled && activeMicProcesses.Contains(t.ProcessName))
                .Select(t => t.ProcessName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var previouslyActive = stateMachine.ActiveTriggerApps
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var name in activeTriggerNames)
            {
                if (!previouslyActive.Contains(name))
                    stateMachine.NotifyTriggerAppActive(name);
            }
            foreach (var name in previouslyActive)
            {
                if (!activeTriggerNames.Contains(name))
                    stateMachine.NotifyTriggerAppInactive(name);
            }

            Console.WriteLine($"  Phase: {stateMachine.Phase}");
            Console.WriteLine($"  Active triggers: {(stateMachine.ActiveTriggerApps.Count > 0 ? string.Join(", ", stateMachine.ActiveTriggerApps) : "none")}");

            switch (stateMachine.Phase)
            {
                case DuckingPhase.Ducking:
                    {
                        var sessions = sessionService.GetAllSessions();
                        duckingService.ApplyDucking(sessions, settings, currentProcessName);
                        var snapshots = snapshotStore.GetAll();
                        Console.WriteLine($"  Ducking: {snapshots.Count} session(s)");
                        foreach (var snap in snapshots)
                            Console.WriteLine($"    {snap.SessionIdentity.ProcessName}: {snap.OriginalVolume:F2} -> {settings.Policy.ComputeDuckedVolume(snap.OriginalVolume):F2}");
                        restoreDueAt = null;
                        break;
                    }
                case DuckingPhase.WaitingForRestore:
                    {
                        if (restoreDueAt == null)
                        {
                            restoreDueAt = DateTimeOffset.Now.AddSeconds(settings.Policy.RestoreDelaySeconds);
                            Console.WriteLine($"  All triggers inactive, restore due at {restoreDueAt.Value:HH:mm:ss}");
                        }
                        else if (DateTimeOffset.Now >= restoreDueAt.Value)
                        {
                            stateMachine.NotifyRestoreDelayElapsed();
                            Console.WriteLine("  Restore delay elapsed, transitioning to Restoring");
                        }
                        else
                        {
                            var remaining = (int)(restoreDueAt.Value - DateTimeOffset.Now).TotalSeconds;
                            Console.WriteLine($"  Waiting for restore... ({remaining}s remaining)");
                        }
                        break;
                    }
                case DuckingPhase.Restoring:
                    {
                        var count = snapshotStore.Count;
                        duckingService.RestoreVolumes();
                        Console.WriteLine($"  Restored: {count} session(s) to original volumes");
                        stateMachine.NotifyRestoreCompleted();
                        restoreDueAt = null;
                        break;
                    }
                case DuckingPhase.Idle:
                    {
                        restoreDueAt = null;
                        break;
                    }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  Error: {ex.Message}");
        }

        if (ctrlCPressed)
            break;

        Thread.Sleep(3000);
    }

    if (snapshotStore.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("Shutting down, restoring volumes...");
        try
        {
            duckingService.RestoreVolumes();
            Console.WriteLine("Volumes restored on shutdown.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  Error during shutdown restore: {ex.Message}");
        }
    }

    Console.WriteLine("VoiceDuck stopped.");
}
