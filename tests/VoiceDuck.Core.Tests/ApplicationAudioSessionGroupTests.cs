namespace VoiceDuck.Core.Tests;

public class ApplicationAudioSessionGroupTests
{
    private const string DefaultDevice = "device-A";

    private static readonly VoiceDuckSettings DefaultSettings = new(
        new DuckingPolicy(0.3, 10),
        new[] { new TriggerApp("Discord.exe") },
        Array.Empty<ExcludeApp>());

    private static AudioSessionIdentity SessionId(uint pid, string name) =>
        new(pid, name, DefaultDevice, $"inst-{pid}");

    private static AudioSessionInfo Session(uint pid, string name, float vol = 0.8f) =>
        new(SessionId(pid, name), vol, false, @"C:\App\" + name.ToLowerInvariant());

    private static AudioSessionInfo SessionWithPath(uint pid, string name, string path, float vol = 0.8f) =>
        new(SessionId(pid, name), vol, false, path);

    [Fact]
    public void GroupSessions_groups_by_device_and_path()
    {
        var classifier = new DuckingSessionClassifier();
        var sessions = new[]
        {
            Session(100, "Chrome.exe", 0.8f),
            Session(200, "Chrome.exe", 0.5f),
        };

        var groups = ApplicationAudioSessionGroup.GroupSessions(
            sessions, "VoiceDuck.exe", classifier, DefaultSettings, DefaultDevice);

        Assert.Single(groups);
        Assert.Equal(2, groups[0].Sessions.Count);
    }

    [Fact]
    public void GroupSessions_different_paths_separate()
    {
        var classifier = new DuckingSessionClassifier();
        var sessions = new[]
        {
            SessionWithPath(100, "Chrome.exe", @"C:\App\chrome.exe", 0.8f),
            SessionWithPath(200, "Spotify.exe", @"C:\App\spotify.exe", 0.5f),
        };

        var groups = ApplicationAudioSessionGroup.GroupSessions(
            sessions, "VoiceDuck.exe", classifier, DefaultSettings, DefaultDevice);

        Assert.Equal(2, groups.Count);
    }

    [Fact]
    public void GroupSessions_excludes_sessions_outside_relevant_endpoint()
    {
        var classifier = new DuckingSessionClassifier();
        var sessions = new[]
        {
            new AudioSessionInfo(new AudioSessionIdentity(100, "Chrome.exe", "device-A", "inst-100"), 0.8f, false, @"C:\App\chrome.exe"),
            new AudioSessionInfo(new AudioSessionIdentity(100, "Chrome.exe", "device-B", "inst-200"), 0.5f, false, @"C:\App\chrome.exe"),
        };

        var groups = ApplicationAudioSessionGroup.GroupSessions(
            sessions, "VoiceDuck.exe", classifier, DefaultSettings, DefaultDevice);

        var group = Assert.Single(groups);
        Assert.Single(group.Sessions);
        Assert.Equal(DefaultDevice, group.Identity.RenderDeviceId);
    }

    [Fact]
    public void GroupSessions_excludes_trigger_apps()
    {
        var classifier = new DuckingSessionClassifier();
        var sessions = new[]
        {
            Session(100, "Discord.exe", 0.8f),
            Session(200, "Chrome.exe", 0.8f),
        };

        var groups = ApplicationAudioSessionGroup.GroupSessions(
            sessions, "VoiceDuck.exe", classifier, DefaultSettings, DefaultDevice);

        Assert.Single(groups);
        Assert.Equal("Chrome.exe", groups[0].Sessions[0].Identity.ProcessName);
    }

    [Fact]
    public void GroupSessions_excludes_voice_duck_itself()
    {
        var classifier = new DuckingSessionClassifier();
        var sessions = new[]
        {
            Session(999, "VoiceDuck.exe", 0.8f),
            Session(200, "Chrome.exe", 0.8f),
        };

        var groups = ApplicationAudioSessionGroup.GroupSessions(
            sessions, "VoiceDuck.exe", classifier, DefaultSettings, DefaultDevice);

        Assert.Single(groups);
    }

    [Fact]
    public void GroupSessions_skips_unresolved()
    {
        var classifier = new DuckingSessionClassifier();
        var sessions = new[]
        {
            new AudioSessionInfo(new AudioSessionIdentity(100, "Chrome.exe", "device-A", ""), 0.8f, false, null),
            Session(200, "Chrome.exe", 0.8f),
        };

        var groups = ApplicationAudioSessionGroup.GroupSessions(
            sessions, "VoiceDuck.exe", classifier, DefaultSettings, DefaultDevice);

        Assert.Single(groups);
    }

    [Fact]
    public void SelectBaseline_returns_max_for_consistent_candidates()
    {
        var sessions = new[]
        {
            Session(100, "Chrome.exe", 0.995f),
            Session(200, "Chrome.exe", 1.0f),
        };

        var classifier = new DuckingSessionClassifier();
        var groups = ApplicationAudioSessionGroup.GroupSessions(
            sessions, "VoiceDuck.exe", classifier, DefaultSettings, DefaultDevice);

        Assert.Single(groups);
        var selected = Assert.IsType<BaselineSelectionResult.Selected>(groups[0].SelectBaseline());
        Assert.Equal(1.0f, selected.Baseline);
    }

    [Fact]
    public void SelectBaseline_single_session()
    {
        var sessions = new[] { Session(100, "Chrome.exe", 0.8f) };
        var classifier = new DuckingSessionClassifier();
        var groups = ApplicationAudioSessionGroup.GroupSessions(
            sessions, "VoiceDuck.exe", classifier, DefaultSettings, DefaultDevice);

        var selected = Assert.IsType<BaselineSelectionResult.Selected>(groups[0].SelectBaseline());
        Assert.Equal(0.8f, selected.Baseline);
    }

    [Fact]
    public void GroupSessions_case_insensitive_path_groups_together()
    {
        var classifier = new DuckingSessionClassifier();
        var sessions = new[]
        {
            SessionWithPath(100, "Chrome.exe", @"C:\App\Chrome.exe", 0.8f),
            SessionWithPath(200, "Chrome.exe", @"c:\app\chrome.exe", 0.5f),
        };

        var groups = ApplicationAudioSessionGroup.GroupSessions(
            sessions, "VoiceDuck.exe", classifier, DefaultSettings, DefaultDevice);

        Assert.Single(groups);
    }
}
