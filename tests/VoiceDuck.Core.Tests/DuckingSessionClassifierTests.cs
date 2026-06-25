namespace VoiceDuck.Core.Tests;

public class DuckingSessionClassifierTests
{
    private static readonly VoiceDuckSettings DefaultSettings = new(
        new DuckingPolicy(), new[] { new TriggerApp("Discord.exe") }, Array.Empty<ExcludeApp>());

    private static readonly VoiceDuckSettings WithExcludes = new(
        new DuckingPolicy(),
        new[] { new TriggerApp("Discord.exe") },
        new[] { new ExcludeApp("ScreenShare.exe") });

    private static AudioSessionInfo Session(uint pid, string name, float vol = 0.5f) =>
        new(new AudioSessionIdentity(pid, name, "default-device", $"inst-{pid}"), vol, false);

    [Fact]
    public void Classify_trigger_app_returns_protect()
    {
        var classifier = new DuckingSessionClassifier();
        var session = Session(100, "Discord.exe");
        var decision = classifier.Classify(session, DefaultSettings, "VoiceDuck.exe");
        Assert.Equal(DuckingOutcome.Protect, decision.Outcome);
    }

    [Fact]
    public void Classify_trigger_app_case_insensitive()
    {
        var classifier = new DuckingSessionClassifier();
        var session = Session(100, "discord.EXE");
        var decision = classifier.Classify(session, DefaultSettings, "VoiceDuck.exe");
        Assert.Equal(DuckingOutcome.Protect, decision.Outcome);
    }

    [Fact]
    public void Classify_exclude_app_returns_protect()
    {
        var classifier = new DuckingSessionClassifier();
        var session = Session(200, "ScreenShare.exe");
        var decision = classifier.Classify(session, WithExcludes, "VoiceDuck.exe");
        Assert.Equal(DuckingOutcome.Protect, decision.Outcome);
    }

    [Fact]
    public void Classify_voice_duck_itself_returns_protect()
    {
        var classifier = new DuckingSessionClassifier();
        var session = Session(999, "VoiceDuck.exe");
        var decision = classifier.Classify(session, DefaultSettings, "VoiceDuck.exe");
        Assert.Equal(DuckingOutcome.Protect, decision.Outcome);
    }

    [Fact]
    public void Classify_voice_duck_itself_case_insensitive()
    {
        var classifier = new DuckingSessionClassifier();
        var session = Session(999, "voiceduck.EXE");
        var decision = classifier.Classify(session, DefaultSettings, "VoiceDuck.exe");
        Assert.Equal(DuckingOutcome.Protect, decision.Outcome);
    }

    [Fact]
    public void Classify_other_app_returns_duck()
    {
        var classifier = new DuckingSessionClassifier();
        var session = Session(300, "Chrome.exe");
        var decision = classifier.Classify(session, DefaultSettings, "VoiceDuck.exe");
        Assert.Equal(DuckingOutcome.Duck, decision.Outcome);
    }

    [Fact]
    public void Classify_other_app_with_no_trigger_apps_returns_duck()
    {
        var classifier = new DuckingSessionClassifier();
        var emptySettings = new VoiceDuckSettings(
            new DuckingPolicy(), Array.Empty<TriggerApp>(), Array.Empty<ExcludeApp>());
        var session = Session(300, "Chrome.exe");
        var decision = classifier.Classify(session, emptySettings, "VoiceDuck.exe");
        Assert.Equal(DuckingOutcome.Duck, decision.Outcome);
    }

    [Fact]
    public void Classify_uses_enabled_trigger_apps_only()
    {
        var classifier = new DuckingSessionClassifier();
        var settings = new VoiceDuckSettings(
            new DuckingPolicy(),
            new[]
            {
                new TriggerApp("Discord.exe") { Enabled = false },
                new TriggerApp("DiscordCanary.exe"),
            },
            Array.Empty<ExcludeApp>());
        var disabled = Session(100, "Discord.exe");
        var enabled = Session(200, "DiscordCanary.exe");
        Assert.Equal(DuckingOutcome.Duck, classifier.Classify(disabled, settings, "VoiceDuck.exe").Outcome);
        Assert.Equal(DuckingOutcome.Protect, classifier.Classify(enabled, settings, "VoiceDuck.exe").Outcome);
    }

    [Fact]
    public void Classify_reason_contains_process_name()
    {
        var classifier = new DuckingSessionClassifier();
        var session = Session(300, "Chrome.exe");
        var decision = classifier.Classify(session, DefaultSettings, "VoiceDuck.exe");
        Assert.Contains("Chrome.exe", decision.Reason);
    }
}
