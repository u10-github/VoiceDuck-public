using System.Drawing;
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

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var logDirectory = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VoiceDuck",
            "logs");
        _logger = new SimpleLogger(logDirectory);
        _logger.Info("Application starting");

        var settingsPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VoiceDuck",
            "settings.json");

        _orchestrator = new DuckingOrchestrator(settingsPath, _logger);
        _orchestrator.PhaseChanged += OnPhaseChanged;

        _notifyIcon = new NotifyIcon
        {
            Icon = Icon.ExtractAssociatedIcon(
                System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName),
            Text = "VoiceDuck",
            Visible = true
        };

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

        _orchestrator.Start();

        _mainWindow = new MainWindow();
        _mainWindow.SetSettingsInfo(_orchestrator.Settings.Policy.DuckingRatio.ToString("F2"));
        _mainWindow.Orchestrator = _orchestrator;
        _mainWindow.Show();
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
}
