using System.Windows;
using System.Windows.Controls;
using VoiceDuck.Core;
using VoiceDuck.Infrastructure;

namespace VoiceDuck.App.Wpf;

public partial class SettingsWindow
{
    private readonly string _settingsPath;

    public SettingsWindow(VoiceDuckSettings currentSettings, string settingsPath)
    {
        InitializeComponent();
        _settingsPath = settingsPath;
        LoadSettings(currentSettings);
    }

    private void LoadSettings(VoiceDuckSettings settings)
    {
        RatioBox.Text = settings.Policy.DuckingRatio.ToString("F2");
        DelayBox.Text = settings.Policy.RestoreDelaySeconds.ToString();

        TriggerPanel.Children.Clear();
        foreach (var trigger in settings.TriggerApps)
        {
            var checkBox = new System.Windows.Controls.CheckBox
            {
                Content = $"{trigger.DisplayName} ({trigger.ProcessName})",
                IsChecked = trigger.Enabled,
                Tag = trigger,
                Margin = new Thickness(0, 2, 0, 2)
            };
            TriggerPanel.Children.Add(checkBox);
        }

        ExcludeList.Items.Clear();
        foreach (var exclude in settings.ExcludeApps)
        {
            var item = new ListBoxItem
            {
                Content = exclude.ProcessName,
                Tag = exclude
            };
            ExcludeList.Items.Add(item);
        }
    }

    private void AddExclude_Click(object sender, RoutedEventArgs e)
    {
        var name = ExcludeNameBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return;

        if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            name += ".exe";

        foreach (var existing in ExcludeList.Items)
        {
            if (existing is ListBoxItem item &&
                string.Equals(item.Content?.ToString(), name, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        ExcludeList.Items.Add(new ListBoxItem
        {
            Content = name,
            Tag = new ExcludeApp(name)
        });

        ExcludeNameBox.Clear();
    }

    private void RemoveExclude_Click(object sender, RoutedEventArgs e)
    {
        if (ExcludeList.SelectedItem is ListBoxItem)
            ExcludeList.Items.Remove(ExcludeList.SelectedItem);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(RatioBox.Text, out var ratio) || ratio < 0.0 || ratio > 1.0)
        {
            System.Windows.MessageBox.Show("Ducking Ratio must be between 0.0 and 1.0.", "Invalid Input",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(DelayBox.Text, out var delay) || delay < 0)
        {
            System.Windows.MessageBox.Show("Restore Delay must be a non-negative integer.", "Invalid Input",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var triggerApps = new List<TriggerApp>();
        foreach (var child in TriggerPanel.Children)
        {
            if (child is System.Windows.Controls.CheckBox checkBox && checkBox.Tag is TriggerApp original)
            {
                triggerApps.Add(new TriggerApp(original.ProcessName, original.DisplayName)
                {
                    Enabled = checkBox.IsChecked ?? true
                });
            }
        }

        var excludeApps = new List<ExcludeApp>();
        foreach (var item in ExcludeList.Items)
        {
            if (item is ListBoxItem listItem && listItem.Tag is ExcludeApp exclude)
            {
                excludeApps.Add(new ExcludeApp(exclude.ProcessName));
            }
        }

        var policy = new DuckingPolicy(ratio, delay);
        var settings = new VoiceDuckSettings(policy, triggerApps, excludeApps);

        var repository = new SettingsRepository(_settingsPath);
        repository.Save(settings);

        if (System.Windows.Application.Current.MainWindow is MainWindow mw && mw.Orchestrator != null)
        {
            mw.Orchestrator.ReloadSettings();
            mw.SetSettingsInfo(ratio.ToString("F2"));
        }

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
