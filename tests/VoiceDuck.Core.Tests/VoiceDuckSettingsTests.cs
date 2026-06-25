namespace VoiceDuck.Core.Tests;

public class VoiceDuckSettingsTests
{
    [Fact]
    public void CreateDefault_returns_non_null()
    {
        var settings = VoiceDuckSettings.CreateDefault();
        Assert.NotNull(settings);
    }

    [Fact]
    public void CreateDefault_has_default_policy()
    {
        var settings = VoiceDuckSettings.CreateDefault();
        Assert.Equal(0.3, settings.Policy.DuckingRatio);
        Assert.Equal(10, settings.Policy.RestoreDelaySeconds);
    }

    [Fact]
    public void CreateDefault_includes_Discord_exe()
    {
        var settings = VoiceDuckSettings.CreateDefault();
        Assert.Contains(settings.TriggerApps, t => t.ProcessName == "Discord.exe");
    }

    [Fact]
    public void CreateDefault_includes_DiscordCanary_exe()
    {
        var settings = VoiceDuckSettings.CreateDefault();
        Assert.Contains(settings.TriggerApps, t => t.ProcessName == "DiscordCanary.exe");
    }

    [Fact]
    public void CreateDefault_includes_DiscordPTB_exe()
    {
        var settings = VoiceDuckSettings.CreateDefault();
        Assert.Contains(settings.TriggerApps, t => t.ProcessName == "DiscordPTB.exe");
    }

    [Fact]
    public void CreateDefault_trigger_apps_all_enabled()
    {
        var settings = VoiceDuckSettings.CreateDefault();
        Assert.All(settings.TriggerApps, t => Assert.True(t.Enabled));
    }

    [Fact]
    public void CreateDefault_trigger_apps_have_display_names()
    {
        var settings = VoiceDuckSettings.CreateDefault();
        Assert.Contains(settings.TriggerApps, t => t.DisplayName == "Discord");
        Assert.Contains(settings.TriggerApps, t => t.DisplayName == "Discord Canary");
        Assert.Contains(settings.TriggerApps, t => t.DisplayName == "Discord PTB");
    }

    [Fact]
    public void CreateDefault_exclude_apps_empty()
    {
        var settings = VoiceDuckSettings.CreateDefault();
        Assert.Empty(settings.ExcludeApps);
    }

    [Fact]
    public void CreateDefault_has_three_trigger_apps()
    {
        var settings = VoiceDuckSettings.CreateDefault();
        Assert.Equal(3, settings.TriggerApps.Count);
    }

    [Fact]
    public void Custom_settings_preserve_values()
    {
        var policy = new DuckingPolicy(0.5, 15);
        var triggerApps = new[] { new TriggerApp("Custom.exe", "Custom") };
        var excludeApps = new[] { new ExcludeApp("Game.exe") };

        var settings = new VoiceDuckSettings(policy, triggerApps, excludeApps);

        Assert.Equal(0.5, settings.Policy.DuckingRatio);
        Assert.Equal(15, settings.Policy.RestoreDelaySeconds);
        Assert.Single(settings.TriggerApps);
        Assert.Single(settings.ExcludeApps);
    }

    [Fact]
    public void CreateSafeFallback_has_no_trigger_apps()
    {
        var settings = VoiceDuckSettings.CreateSafeFallback();
        Assert.Empty(settings.TriggerApps);
    }

    [Fact]
    public void CreateSafeFallback_has_no_exclude_apps()
    {
        var settings = VoiceDuckSettings.CreateSafeFallback();
        Assert.Empty(settings.ExcludeApps);
    }

    [Fact]
    public void CreateSafeFallback_ratio_is_1_0()
    {
        var settings = VoiceDuckSettings.CreateSafeFallback();
        Assert.Equal(1.0, settings.Policy.DuckingRatio);
    }
}
