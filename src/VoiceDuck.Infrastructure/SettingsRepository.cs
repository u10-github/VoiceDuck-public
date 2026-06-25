using System.Text.Json;
using VoiceDuck.Core;

namespace VoiceDuck.Infrastructure;

public class SettingsRepository
{
    private readonly string _filePath;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public SettingsRepository(string filePath)
    {
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
    }

    public VoiceDuckSettings Load()
    {
        var json = File.ReadAllText(_filePath);
        var dto = JsonSerializer.Deserialize<SettingsDto>(json, JsonOptions);
        return ToDomain(dto ?? new SettingsDto());
    }

    public VoiceDuckSettings LoadOrDefault()
    {
        if (!File.Exists(_filePath))
            return VoiceDuckSettings.CreateDefault();

        try
        {
            return Load();
        }
        catch
        {
            return VoiceDuckSettings.CreateSafeFallback();
        }
    }

    public void Save(VoiceDuckSettings settings)
    {
        var dto = ToDto(settings);
        var json = JsonSerializer.Serialize(dto, JsonOptions);
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(_filePath, json);
    }

    private static VoiceDuckSettings ToDomain(SettingsDto dto)
    {
        var policy = new DuckingPolicy(dto.DuckingRatio, dto.RestoreDelaySeconds);
        var triggerApps = (dto.TriggerApps ?? [])
            .Select(t => new TriggerApp(t.ProcessName ?? "", t.DisplayName ?? "")
            {
                Enabled = t.Enabled
            })
            .ToList();
        var excludeApps = (dto.ExcludeApps ?? [])
            .Select(e => new ExcludeApp(e.ProcessName ?? ""))
            .ToList();
        return new VoiceDuckSettings(policy, triggerApps, excludeApps);
    }

    private static SettingsDto ToDto(VoiceDuckSettings settings)
    {
        return new SettingsDto
        {
            DuckingRatio = settings.Policy.DuckingRatio,
            RestoreDelaySeconds = settings.Policy.RestoreDelaySeconds,
            TriggerApps = settings.TriggerApps.Select(t => new TriggerAppDto
            {
                DisplayName = t.DisplayName,
                ProcessName = t.ProcessName,
                Enabled = t.Enabled
            }).ToList(),
            ExcludeApps = settings.ExcludeApps.Select(e => new ExcludeAppDto
            {
                ProcessName = e.ProcessName
            }).ToList()
        };
    }

    private class SettingsDto
    {
        public double DuckingRatio { get; set; }
        public int RestoreDelaySeconds { get; set; }
        public List<TriggerAppDto>? TriggerApps { get; set; }
        public List<ExcludeAppDto>? ExcludeApps { get; set; }
    }

    private class TriggerAppDto
    {
        public string DisplayName { get; set; } = "";
        public string ProcessName { get; set; } = "";
        public bool Enabled { get; set; }
    }

    private class ExcludeAppDto
    {
        public string ProcessName { get; set; } = "";
    }
}
