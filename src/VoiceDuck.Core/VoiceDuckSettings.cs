namespace VoiceDuck.Core;

public record VoiceDuckSettings
{
    public DuckingPolicy Policy { get; }
    public IReadOnlyList<TriggerApp> TriggerApps { get; }
    public IReadOnlyList<ExcludeApp> ExcludeApps { get; }

    public VoiceDuckSettings(
        DuckingPolicy policy,
        IEnumerable<TriggerApp> triggerApps,
        IEnumerable<ExcludeApp> excludeApps)
    {
        Policy = policy;
        TriggerApps = triggerApps?.ToList() ?? throw new ArgumentNullException(nameof(triggerApps));
        ExcludeApps = excludeApps?.ToList() ?? throw new ArgumentNullException(nameof(excludeApps));
    }

    public static VoiceDuckSettings CreateDefault() => new(
        new DuckingPolicy(),
        new[]
        {
            new TriggerApp("Discord.exe", "Discord"),
            new TriggerApp("DiscordCanary.exe", "Discord Canary"),
            new TriggerApp("DiscordPTB.exe", "Discord PTB"),
        },
        Array.Empty<ExcludeApp>()
    );

    public static VoiceDuckSettings CreateSafeFallback() => new(
        new DuckingPolicy(1.0, 10),
        Array.Empty<TriggerApp>(),
        Array.Empty<ExcludeApp>()
    );
}
