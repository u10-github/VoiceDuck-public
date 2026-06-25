using System.ComponentModel;
using System.Windows;
using VoiceDuck.Core;

namespace VoiceDuck.App.Wpf;

public partial class MainWindow
{
    public bool AllowClose { get; set; }
    public DuckingOrchestrator? Orchestrator { get; set; }

    public MainWindow()
    {
        InitializeComponent();
    }

    public void UpdateState(DuckingPhase phase, IReadOnlySet<string> activeTriggers)
    {
        var isManual = Orchestrator?.IsManualDucking ?? false;

        StatusText.Text = isManual
            ? "Manual Ducking (auto detection paused)"
            : phase switch
            {
                DuckingPhase.Idle => "Idle - Waiting for trigger app",
                DuckingPhase.Ducking => "Ducking - Lowering non-trigger volumes",
                DuckingPhase.WaitingForRestore => "WaitingForRestore - Trigger stopped, waiting to restore",
                DuckingPhase.Restoring => "Restoring - Returning volumes to original",
                _ => phase.ToString()
            };

        TriggersText.Text = activeTriggers.Count > 0
            ? string.Join(", ", activeTriggers)
            : "(none)";

        ManualDuckButton.Content = isManual ? "Restore" : "Force Duck";
    }

    public void SetSettingsInfo(string ratioText)
    {
        RatioText.Text = ratioText;
    }

    private void ManualDuckButton_Click(object sender, RoutedEventArgs e)
    {
        if (Orchestrator == null)
            return;

        if (Orchestrator.IsManualDucking)
            Orchestrator.ManualDuckOff();
        else
            Orchestrator.ManualDuckOn();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (Orchestrator == null)
            return;

        var window = new SettingsWindow(Orchestrator.Settings, Orchestrator.SettingsPath);
        window.Owner = this;
        window.ShowDialog();
    }

    private void ViewLogButton_Click(object sender, RoutedEventArgs e)
    {
        var logDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VoiceDuck",
            "logs");
        if (System.IO.Directory.Exists(logDir))
            System.Diagnostics.Process.Start("explorer.exe", logDir);
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (AllowClose)
            return;

        e.Cancel = true;
        Hide();
    }
}
