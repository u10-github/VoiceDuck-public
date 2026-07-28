namespace VoiceDuck.Core.Tests;

public class DuckingSessionClassifierTests
{
    private const string RelevantEndpoint = "default-device";
    private const string ExecutablePath = @"C:\Apps\app.exe";

    private static readonly VoiceDuckSettings DefaultSettings = new(
        new DuckingPolicy(),
        new[] { new TriggerApp("Discord.exe") },
        Array.Empty<ExcludeApp>());

    private static readonly VoiceDuckSettings WithExcludes = new(
        new DuckingPolicy(),
        new[] { new TriggerApp("Discord.exe") },
        new[] { new ExcludeApp("ScreenShare.exe") });

    private static AudioSessionInfo Session(
        uint pid,
        string name,
        string device = RelevantEndpoint,
        string instance = "resolved-instance",
        string? path = ExecutablePath) =>
        new(new AudioSessionIdentity(pid, name, device, instance), 0.5f, false, path);

    [Fact]
    public void Classify_rejects_unresolved_identity()
    {
        var result = Classify(Session(100, "Game.exe", instance: ""));

        AssertRejected(result, ControlEligibilityRejectionReason.UnresolvedIdentity);
    }

    [Fact]
    public void Classify_rejects_process_id_zero()
    {
        var result = Classify(Session(0, "SystemSounds"));

        AssertRejected(result, ControlEligibilityRejectionReason.InvalidProcessId);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void Classify_rejects_missing_process_name(string processName)
    {
        var result = Classify(Session(100, processName));

        AssertRejected(result, ControlEligibilityRejectionReason.MissingProcessName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void Classify_rejects_missing_executable_path(string? path)
    {
        var result = Classify(Session(100, "Game.exe", path: path));

        AssertRejected(result, ControlEligibilityRejectionReason.MissingExecutablePath);
    }

    [Fact]
    public void Classify_rejects_irrelevant_endpoint()
    {
        var result = Classify(Session(100, "Game.exe", device: "other-device"));

        AssertRejected(result, ControlEligibilityRejectionReason.IrrelevantEndpoint);
    }

    [Fact]
    public void Classify_rejects_trigger_application_case_insensitively()
    {
        var result = Classify(Session(100, "discord.EXE"));

        AssertRejected(result, ControlEligibilityRejectionReason.TriggerApplication);
    }

    [Fact]
    public void Classify_rejects_voice_duck_itself_case_insensitively()
    {
        var result = Classify(Session(999, "voiceduck.EXE"));

        AssertRejected(result, ControlEligibilityRejectionReason.Self);
    }

    [Fact]
    public void Classify_rejects_user_excluded_application()
    {
        var result = Classify(
            Session(200, "ScreenShare.exe"),
            WithExcludes);

        AssertRejected(result, ControlEligibilityRejectionReason.UserExcluded);
    }

    [Theory]
    [InlineData("GGST.exe")]
    [InlineData("steam.exe")]
    [InlineData("explorer.exe")]
    public void Classify_accepts_ordinary_application_without_name_based_exceptions(string processName)
    {
        var result = Classify(Session(300, processName));

        Assert.IsType<ControlEligibilityResult.Eligible>(result);
    }

    [Fact]
    public void Classify_uses_enabled_trigger_apps_only()
    {
        var settings = new VoiceDuckSettings(
            new DuckingPolicy(),
            new[]
            {
                new TriggerApp("Discord.exe") { Enabled = false },
                new TriggerApp("DiscordCanary.exe"),
            },
            Array.Empty<ExcludeApp>());

        Assert.IsType<ControlEligibilityResult.Eligible>(
            Classify(Session(100, "Discord.exe"), settings));
        AssertRejected(
            Classify(Session(200, "DiscordCanary.exe"), settings),
            ControlEligibilityRejectionReason.TriggerApplication);
    }

    private static ControlEligibilityResult Classify(
        AudioSessionInfo session,
        VoiceDuckSettings? settings = null)
    {
        var classifier = new DuckingSessionClassifier();
        return classifier.Classify(
            session,
            RelevantEndpoint,
            settings ?? DefaultSettings,
            "VoiceDuck.exe");
    }

    private static void AssertRejected(
        ControlEligibilityResult result,
        ControlEligibilityRejectionReason reason)
    {
        var rejected = Assert.IsType<ControlEligibilityResult.Rejected>(result);
        Assert.Equal(reason, rejected.Reason);
    }
}
