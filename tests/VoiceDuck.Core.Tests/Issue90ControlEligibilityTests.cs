namespace VoiceDuck.Core.Tests;

public class Issue90ControlEligibilityTests
{
    private const string Device = "default-device";
    private const string ValidPath = @"C:\Games\GGST.exe";

    private static readonly VoiceDuckSettings Settings = new(
        new DuckingPolicy(0.5, 10),
        new[] { new TriggerApp("Discord.exe") },
        new[] { new ExcludeApp("Excluded.exe") });

    [Fact]
    public void Rejected_sessions_create_no_state_obligation_or_volume_write()
    {
        var fixture = new Fixture();
        var sessions = new[]
        {
            Session(0, "ZeroPid.exe", path: @"C:\Apps\zero.exe"),
            Session(2, "Unresolved.exe", instance: "", path: @"C:\Apps\unresolved.exe"),
            Session(8, "", path: @"C:\Apps\missing-name.exe"),
            Session(9, " ", path: @"C:\Apps\blank-name.exe"),
            Session(3, "MissingPath.exe", path: " "),
            Session(4, "OtherEndpoint.exe", device: "other-device", path: @"C:\Apps\other.exe"),
            Session(5, "Discord.exe", path: @"C:\Apps\discord.exe"),
            Session(6, "VoiceDuck.exe", path: @"C:\Apps\voiceduck.exe"),
            Session(7, "Excluded.exe", path: @"C:\Apps\excluded.exe"),
        };

        fixture.Service.ApplyDucking(sessions, Settings, "VoiceDuck.exe");

        Assert.Equal(0, fixture.Store.Count);
        Assert.Equal(0, fixture.Repository.SaveCount);
        Assert.Empty(fixture.Writer.Calls);
    }

    [Fact]
    public void Baseline_candidates_are_formed_only_from_eligible_sessions()
    {
        var fixture = new Fixture();

        fixture.Service.ApplyDucking(
            new[]
            {
                Session(1, "GGST.exe", volume: 0.8f, path: ValidPath),
                Session(2, "GGST.exe", volume: 0.2f, path: " "),
                Session(3, " ", volume: 0.1f, path: ValidPath),
            },
            Settings,
            "VoiceDuck.exe");

        var identity = new ApplicationAudioIdentity(Device, ValidPath);
        Assert.True(fixture.Store.TryGet(identity, out var state));
        Assert.Equal(0.8f, state!.BaselineVolume);
        Assert.Single(fixture.Repository.Saved);
        Assert.Single(fixture.Writer.Calls);
        Assert.Equal((uint)1, fixture.Writer.Calls[0].Identity.ProcessId);
    }

    [Fact]
    public void Rejection_reason_is_logged_once_for_identical_poll_state()
    {
        var fixture = new Fixture();
        var sessions = new[] { Session(0, "ZeroPid.exe", path: @"C:\Apps\zero.exe") };

        fixture.Service.ApplyDucking(sessions, Settings, "VoiceDuck.exe");
        fixture.Service.ApplyDucking(sessions, Settings, "VoiceDuck.exe");

        var rejectionLogs = fixture.Logger.Messages
            .Where(message => message.StartsWith("ControlEligibility:", StringComparison.Ordinal))
            .ToList();
        var rejection = Assert.Single(rejectionLogs);
        Assert.Contains("eligible=false", rejection);
        Assert.Contains("reason=InvalidProcessId", rejection);
    }

    [Fact]
    public void Missing_process_name_is_logged_once_for_identical_poll_state()
    {
        var fixture = new Fixture();
        var sessions = new[] { Session(1, " ", path: @"C:\Apps\unknown.exe") };

        fixture.Service.ApplyDucking(sessions, Settings, "VoiceDuck.exe");
        fixture.Service.ApplyDucking(sessions, Settings, "VoiceDuck.exe");

        var rejectionLogs = fixture.Logger.Messages
            .Where(message => message.StartsWith("ControlEligibility:", StringComparison.Ordinal))
            .ToList();
        var rejection = Assert.Single(rejectionLogs);
        Assert.Contains("eligible=false", rejection);
        Assert.Contains("reason=MissingProcessName", rejection);
    }

    [Fact]
    public void Rejection_reason_change_is_logged_as_a_new_state()
    {
        var fixture = new Fixture();

        fixture.Service.ApplyDucking(
            new[] { Session(1, "App.exe", path: null) },
            Settings,
            "VoiceDuck.exe");
        fixture.Service.ApplyDucking(
            new[] { Session(1, "App.exe", device: "other-device", path: @"C:\Apps\app.exe") },
            Settings,
            "VoiceDuck.exe");

        var rejectionLogs = fixture.Logger.Messages
            .Where(message => message.StartsWith("ControlEligibility:", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(2, rejectionLogs.Count);
        Assert.Contains(rejectionLogs, message => message.Contains("reason=MissingExecutablePath"));
        Assert.Contains(rejectionLogs, message => message.Contains("reason=IrrelevantEndpoint"));
    }

    [Fact]
    public void Equivalent_rejection_set_in_different_order_does_not_repeat_logs()
    {
        var fixture = new Fixture();
        var sessions = new[]
        {
            Session(0, "ZeroPid.exe", path: @"C:\Apps\zero.exe"),
            Session(2, "MissingPath.exe", path: " "),
        };

        fixture.Service.ApplyDucking(sessions, Settings, "VoiceDuck.exe");
        fixture.Service.ApplyDucking(sessions.Reverse().ToArray(), Settings, "VoiceDuck.exe");

        Assert.Equal(
            2,
            fixture.Logger.Messages.Count(message =>
                message.StartsWith("ControlEligibility:", StringComparison.Ordinal)));
    }

    [Fact]
    public void Rejection_is_logged_again_after_session_disappears_and_reappears()
    {
        var fixture = new Fixture();
        var sessions = new[] { Session(0, "ZeroPid.exe", path: @"C:\Apps\zero.exe") };

        fixture.Service.ApplyDucking(sessions, Settings, "VoiceDuck.exe");
        fixture.Service.ApplyDucking(Array.Empty<AudioSessionInfo>(), Settings, "VoiceDuck.exe");
        fixture.Service.ApplyDucking(sessions, Settings, "VoiceDuck.exe");

        Assert.Equal(
            2,
            fixture.Logger.Messages.Count(message =>
                message.StartsWith("ControlEligibility:", StringComparison.Ordinal)));
    }

    [Fact]
    public void Distinct_session_field_tuples_do_not_collide_in_rejection_cache()
    {
        var fixture = new Fixture();
        var sessions = new[]
        {
            Session(0, "a|b", device: "c", instance: "same", path: @"C:\Apps\same.exe"),
            Session(0, "a", device: "b|c", instance: "same", path: @"C:\Apps\same.exe"),
        };
        fixture.Selector.EndpointId = null;

        fixture.Service.ApplyDucking(sessions, Settings, "VoiceDuck.exe");

        Assert.Equal(
            2,
            fixture.Logger.Messages.Count(message =>
                message.StartsWith("ControlEligibility:", StringComparison.Ordinal)));
    }

    [Fact]
    public void Missing_relevant_endpoint_rejects_valid_session_without_effects()
    {
        var fixture = new Fixture();
        fixture.Selector.EndpointId = null;

        fixture.Service.ApplyDucking(
            new[] { Session(1, "GGST.exe", path: ValidPath) },
            Settings,
            "VoiceDuck.exe");

        Assert.Equal(0, fixture.Store.Count);
        Assert.Equal(0, fixture.Repository.SaveCount);
        Assert.Empty(fixture.Writer.Calls);
        Assert.Contains(
            fixture.Logger.Messages,
            message => message.StartsWith("ControlEligibility:", StringComparison.Ordinal)
                && message.Contains("reason=IrrelevantEndpoint"));
    }

    [Theory]
    [InlineData("trigger")]
    [InlineData("self")]
    [InlineData("exclude")]
    public void Existing_restoration_obligation_survives_later_eligibility_rejection(
        string rejectionKind)
    {
        var fixture = new Fixture();
        var session = Session(1, "Debt.exe", path: @"C:\Apps\debt.exe");
        var initialSettings = new VoiceDuckSettings(
            new DuckingPolicy(0.5, 10),
            Array.Empty<TriggerApp>(),
            Array.Empty<ExcludeApp>());

        fixture.Service.ApplyDucking(
            new[] { session },
            initialSettings,
            "VoiceDuck.exe");
        var saveCountBeforeRejection = fixture.Repository.SaveCount;
        fixture.Writer.Calls.Clear();

        var rejectedSettings = rejectionKind switch
        {
            "trigger" => new VoiceDuckSettings(
                new DuckingPolicy(0.5, 10),
                new[] { new TriggerApp("Debt.exe") },
                Array.Empty<ExcludeApp>()),
            "exclude" => new VoiceDuckSettings(
                new DuckingPolicy(0.5, 10),
                Array.Empty<TriggerApp>(),
                new[] { new ExcludeApp("Debt.exe") }),
            _ => initialSettings,
        };
        var currentProcessName = rejectionKind == "self"
            ? "Debt.exe"
            : "VoiceDuck.exe";

        fixture.Service.ApplyDucking(
            new[] { session },
            rejectedSettings,
            currentProcessName);

        Assert.Equal(saveCountBeforeRejection, fixture.Repository.SaveCount);
        Assert.Single(fixture.Repository.Saved);
        Assert.Equal(1, fixture.Store.Count);
        Assert.Empty(fixture.Writer.Calls);

        fixture.Service.RestoreVolumes(new[] { session });

        Assert.Equal(0, fixture.Store.Count);
        Assert.Empty(fixture.Repository.Saved);
        var restoreWrite = Assert.Single(fixture.Writer.Calls);
        Assert.Equal(0.8f, restoreWrite.Volume);
    }

    private static AudioSessionInfo Session(
        uint pid,
        string processName,
        float volume = 0.8f,
        string device = Device,
        string instance = "resolved",
        string? path = ValidPath) =>
        new(
            new AudioSessionIdentity(
                pid,
                processName,
                device,
                string.IsNullOrEmpty(instance) ? instance : $"{instance}-{pid}"),
            volume,
            false,
            path);

    private sealed class Fixture
    {
        public WriterMock Writer { get; } = new();
        public ApplicationVolumeStateStore Store { get; } = new();
        public RepositoryMock Repository { get; } = new();
        public TestLogger Logger { get; } = new();
        public EndpointSelectorMock Selector { get; } = new();
        public VolumeDuckingService Service { get; }

        public Fixture()
        {
            Service = new VolumeDuckingService(
                Writer,
                new DuckingSessionClassifier(),
                Store,
                Repository,
                Selector,
                Logger);
        }
    }

    private sealed class WriterMock : IAudioSessionVolumeWriter
    {
        public List<(AudioSessionIdentity Identity, float Volume)> Calls { get; } = new();

        public VolumeWriteResult SetVolume(AudioSessionIdentity identity, float volume)
        {
            Calls.Add((identity, volume));
            return VolumeWriteResult.Succeeded;
        }
    }

    private sealed class RepositoryMock : IRestorationObligationRepository
    {
        public int SaveCount { get; private set; }
        public List<RestorationObligation> Saved { get; private set; } = new();

        public RestorationObligationLoadResult LoadAll() =>
            new(Saved.ToArray(), WasCorrupt: false);

        public void SaveAll(IReadOnlyList<RestorationObligation> obligations)
        {
            SaveCount++;
            Saved = obligations.ToList();
        }

        public void DeleteAll() => Saved.Clear();
    }

    private sealed class EndpointSelectorMock : IAudioEndpointSelector
    {
        public string? EndpointId { get; set; } = Device;
        public string? GetDefaultMultimediaEndpointId() => EndpointId;
    }

    private sealed class TestLogger : ILogger
    {
        public List<string> Messages { get; } = new();
        public void Info(string message) => Messages.Add(message);
        public void Warn(string message) => Messages.Add(message);
        public void Error(string message) => Messages.Add(message);
    }
}
