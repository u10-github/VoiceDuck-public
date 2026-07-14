using System.Diagnostics;
using System.Drawing;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Forms;
using VoiceDuck.Core;

namespace VoiceDuck.App.Wpf;

public partial class App
{
    private DuckingOrchestrator? _orchestrator;
    private SimpleLogger? _logger;
    private NotifyIcon? _notifyIcon;
    private MainWindow? _mainWindow;
    private ToolStripMenuItem? _manualDuckItem;

    private string _startupMarker = "none";
    private string _crashLogPath = "startup-crash.log";

    public App()
    {
        ResolveCrashLogPath();

        DispatcherUnhandledException += (_, e) =>
        {
            WriteCrashLog("DispatcherUnhandledException", e.Exception);
            Environment.Exit(1);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            WriteCrashLog("AppDomain.UnhandledException", ex ?? new Exception("Non-exception object"));
            Environment.Exit(1);
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            WriteCrashLog("TaskScheduler.UnobservedTaskException", e.Exception);
            e.SetObserved();
        };
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        _startupMarker = "startup.01: base.OnStartup";
        base.OnStartup(e);

        try
        {
            _startupMarker = "startup.02: logger init";
            var logDirectory = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "VoiceDuck",
                "logs");
            _logger = new SimpleLogger(logDirectory);
            _logger.Info("Application starting");

            _startupMarker = "startup.03: settings path";
            var settingsPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "VoiceDuck",
                "settings.json");
            _logger.Info($"startup.03: settings={settingsPath}");

            _startupMarker = "startup.04: orchestrator construct";
            _orchestrator = new DuckingOrchestrator(settingsPath, _logger);
            _orchestrator.PhaseChanged += OnPhaseChanged;
            _logger.Info("startup.04: orchestrator constructed");

            _startupMarker = "startup.05: icon extraction";
            var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName;
            Icon icon;
            try
            {
                icon = Icon.ExtractAssociatedIcon(exePath) ?? SystemIcons.Application;
                _logger.Info($"startup.05: icon extracted from {exePath}");
            }
            catch (Exception ex)
            {
                _logger.Error($"startup.05: ExtractAssociatedIcon failed for {exePath}", ex);
                icon = SystemIcons.Application;
            }

            _startupMarker = "startup.06: NotifyIcon";
            _notifyIcon = new NotifyIcon
            {
                Icon = icon,
                Text = "VoiceDuck",
                Visible = true
            };
            _logger.Info("startup.06: NotifyIcon created");

            _startupMarker = "startup.07: context menu";
            _manualDuckItem = new ToolStripMenuItem("Duck ON");
            _manualDuckItem.Click += OnManualDuckClick;

            var settingsItem = new ToolStripMenuItem("Settings...");
            settingsItem.Click += (_, _) => OpenSettingsWindow();

            var viewLogItem = new ToolStripMenuItem("View Log");
            viewLogItem.Click += (_, _) => OpenLogFolder();

            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("Show", null, (_, _) => ShowMainWindow());
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add(_manualDuckItem);
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add(settingsItem);
            contextMenu.Items.Add(viewLogItem);
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add("Exit", null, (_, _) => ExitApp());
            _notifyIcon.ContextMenuStrip = contextMenu;
            _logger.Info("startup.07: context menu created");

            _startupMarker = "startup.08: orchestrator.Start";
            _orchestrator.Start();
            _logger.Info("startup.08: orchestrator started");

            _startupMarker = "startup.09: MainWindow construct";
            _mainWindow = new MainWindow();
            _logger.Info("startup.09: MainWindow constructed");

            _startupMarker = "startup.10: MainWindow configure";
            _mainWindow.SetSettingsInfo(_orchestrator.Settings.Policy.DuckingRatio.ToString("F2"));
            _mainWindow.Orchestrator = _orchestrator;

            _startupMarker = "startup.11: MainWindow.Show";
            _mainWindow.Show();
            _logger.Info("startup.11: MainWindow shown");

            _startupMarker = "startup.12: startup complete";
            _logger.Info("startup completed successfully");
        }
        catch (Exception ex)
        {
            WriteCrashLog($"OnStartup (marker={_startupMarker})", ex);
            _logger?.Error($"Startup failed at {_startupMarker}", ex);
            throw;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _logger?.Info("Application exiting");
        _orchestrator?.RestoreAndStop();
        _notifyIcon?.Dispose();
        _logger?.Dispose();
        base.OnExit(e);
    }

    private void ShowMainWindow()
    {
        if (_mainWindow == null)
        {
            _mainWindow = new MainWindow();
            _mainWindow.Closed += (_, _) => _mainWindow = null;
            _mainWindow.Orchestrator = _orchestrator;
        }
        _mainWindow.Show();
        _mainWindow.Activate();
    }

    private void ExitApp()
    {
        if (_mainWindow != null)
            _mainWindow.AllowClose = true;

        _orchestrator?.RestoreAndStop();
        _notifyIcon?.Dispose();
        _logger?.Dispose();
        Current.Shutdown();
    }

    private void OpenLogFolder()
    {
        if (_logger == null)
            return;

        try
        {
            System.Diagnostics.Process.Start("explorer.exe", _logger.LogDirectory);
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to open log folder", ex);
        }
    }

    private void OpenSettingsWindow()
    {
        if (_orchestrator == null)
            return;

        var window = new SettingsWindow(_orchestrator.Settings, _orchestrator.SettingsPath);

        if (_mainWindow is { IsVisible: true })
            window.Owner = _mainWindow;
        else if (System.Windows.Application.Current.MainWindow is { IsVisible: true } mw)
            window.Owner = mw;

        window.ShowDialog();
    }

    private void OnManualDuckClick(object? sender, EventArgs e)
    {
        if (_orchestrator == null)
            return;

        if (_orchestrator.IsManualDucking)
            _orchestrator.ManualDuckOff();
        else
            _orchestrator.ManualDuckOn();

        UpdateManualDuckMenuItem();
    }

    private void OnPhaseChanged(DuckingPhase phase, IReadOnlySet<string> activeTriggers)
    {
        _mainWindow?.Dispatcher.Invoke(() =>
        {
            _mainWindow!.UpdateState(phase, activeTriggers);
            UpdateManualDuckMenuItem();
        });
    }

    private void UpdateManualDuckMenuItem()
    {
        if (_manualDuckItem == null || _orchestrator == null)
            return;

        _manualDuckItem.Text = _orchestrator.IsManualDucking ? "Duck OFF" : "Duck ON";
    }

    private void ResolveCrashLogPath()
    {
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var logDir = System.IO.Path.Combine(appData, "VoiceDuck", "logs");
            System.IO.Directory.CreateDirectory(logDir);
            _crashLogPath = System.IO.Path.Combine(logDir, "startup-crash.log");
        }
        catch
        {
            try
            {
                var tempDir = System.IO.Path.GetTempPath();
                _crashLogPath = System.IO.Path.Combine(tempDir, "VoiceDuck-startup-crash.log");
            }
            catch
            {
                // Keep default "startup-crash.log"
            }
        }
    }

    private void WriteCrashLog(string source, Exception ex)
    {
        try
        {
            System.IO.File.AppendAllText(_crashLogPath,
                "=== VoiceDuck Crash Log ===" + Environment.NewLine +
                "Timestamp: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine +
                "Source: " + source + Environment.NewLine +
                "Marker: " + _startupMarker + Environment.NewLine +
                "Exception: " + ex + Environment.NewLine +
                "Executable: " + Environment.ProcessPath + Environment.NewLine +
                "BaseDirectory: " + AppDomain.CurrentDomain.BaseDirectory + Environment.NewLine +
                "CurrentDirectory: " + Environment.CurrentDirectory + Environment.NewLine +
                "Is64BitProcess: " + Environment.Is64BitProcess + Environment.NewLine +
                "OSVersion: " + Environment.OSVersion + Environment.NewLine +
                "CLRVersion: " + Environment.Version + Environment.NewLine +
                "ProcessId: " + Environment.ProcessId + Environment.NewLine +
                "=============================");
        }
        catch
        {
            // Swallow secondary exceptions — must not hide the original failure
        }
    }
}
