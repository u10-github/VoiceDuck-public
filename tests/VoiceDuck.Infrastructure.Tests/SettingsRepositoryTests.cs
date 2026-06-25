using VoiceDuck.Core;

namespace VoiceDuck.Infrastructure.Tests;

public class SettingsRepositoryTests
{
    [Fact]
    public void Save_and_load_roundtrip()
    {
        var path = Path.GetTempFileName();
        try
        {
            var repo = new SettingsRepository(path);
            var original = VoiceDuckSettings.CreateDefault();
            repo.Save(original);

            var loaded = repo.Load();
            Assert.Equal(original.Policy.DuckingRatio, loaded.Policy.DuckingRatio);
            Assert.Equal(original.Policy.RestoreDelaySeconds, loaded.Policy.RestoreDelaySeconds);
            Assert.Equal(3, loaded.TriggerApps.Count);
            Assert.Empty(loaded.ExcludeApps);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Save_and_load_preserves_trigger_app_details()
    {
        var path = Path.GetTempFileName();
        try
        {
            var repo = new SettingsRepository(path);
            var original = VoiceDuckSettings.CreateDefault();
            repo.Save(original);

            var loaded = repo.Load();
            var discord = loaded.TriggerApps.First(t => t.ProcessName == "Discord.exe");
            Assert.Equal("Discord", discord.DisplayName);
            Assert.True(discord.Enabled);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadOrDefault_returns_default_when_file_missing()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            var repo = new SettingsRepository(path);
            var settings = repo.LoadOrDefault();
            Assert.NotNull(settings);
            Assert.Equal(0.3, settings.Policy.DuckingRatio);
            Assert.Equal(3, settings.TriggerApps.Count);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void LoadOrDefault_returns_safe_fallback_when_file_corrupt()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "this is not valid json");
            var repo = new SettingsRepository(path);
            var settings = repo.LoadOrDefault();
            Assert.NotNull(settings);
            Assert.Empty(settings.TriggerApps);
            Assert.Equal(1.0, settings.Policy.DuckingRatio);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Save_creates_directory_if_not_exists()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var path = Path.Combine(dir, "settings.json");
        try
        {
            var repo = new SettingsRepository(path);
            var settings = VoiceDuckSettings.CreateDefault();
            repo.Save(settings);

            Assert.True(Directory.Exists(dir));
            Assert.True(File.Exists(path));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void LoadOrDefault_preserves_custom_values_after_save()
    {
        var path = Path.GetTempFileName();
        try
        {
            var policy = new DuckingPolicy(0.15, 30);
            var triggerApps = new[] { new TriggerApp("Custom.exe", "Custom") };
            var excludeApps = new[] { new ExcludeApp("Game.exe") };
            var custom = new VoiceDuckSettings(policy, triggerApps, excludeApps);

            var repo = new SettingsRepository(path);
            repo.Save(custom);

            var loaded = repo.LoadOrDefault();
            Assert.Equal(0.15, loaded.Policy.DuckingRatio);
            Assert.Equal(30, loaded.Policy.RestoreDelaySeconds);
            Assert.Single(loaded.TriggerApps);
            Assert.Single(loaded.ExcludeApps);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Save_writes_camelCase_json()
    {
        var path = Path.GetTempFileName();
        try
        {
            var repo = new SettingsRepository(path);
            repo.Save(VoiceDuckSettings.CreateDefault());

            var json = File.ReadAllText(path);

            Assert.Contains("\"duckingRatio\"", json);
            Assert.Contains("\"restoreDelaySeconds\"", json);
            Assert.Contains("\"triggerApps\"", json);
            Assert.DoesNotContain("\"DuckingRatio\"", json);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadOrDefault_reads_camelCase_json()
    {
        var path = Path.GetTempFileName();
        try
        {
            var camelCaseJson = "{\"duckingRatio\":0.5,\"restoreDelaySeconds\":20,\"triggerApps\":[{\"displayName\":\"Discord\",\"processName\":\"Discord.exe\",\"enabled\":true}],\"excludeApps\":[]}";
            File.WriteAllText(path, camelCaseJson);
            var repo = new SettingsRepository(path);
            var settings = repo.LoadOrDefault();
            Assert.Equal(0.5, settings.Policy.DuckingRatio);
            Assert.Equal(20, settings.Policy.RestoreDelaySeconds);
            Assert.Single(settings.TriggerApps);
            Assert.Equal("Discord.exe", settings.TriggerApps[0].ProcessName);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
