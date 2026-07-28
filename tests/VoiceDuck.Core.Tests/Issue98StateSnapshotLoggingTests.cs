namespace VoiceDuck.Core.Tests;

public sealed class Issue98StateSnapshotLoggingTests
{
    private const string Endpoint = "endpoint-a";
    private const string Path = @"C:\Games\GGST\ggst.exe";

    [Fact]
    public void Repeated_identical_snapshot_is_logged_only_once()
    {
        var detector = new DuckingStateSnapshotChangeDetector();
        var snapshot = Snapshot();

        Assert.True(detector.ShouldLog(snapshot));
        Assert.False(detector.ShouldLog(Snapshot()));
    }

    [Fact]
    public void Every_meaningful_field_change_is_detected()
    {
        AssertChanged(Snapshot(phase: DuckingPhase.Idle));
        AssertChanged(Snapshot(endpoint: "endpoint-b"));
        AssertChanged(Snapshot(triggers: new[] { "Discord.exe", "DiscordCanary.exe" }));
        AssertChanged(Snapshot(applications: Array.Empty<DuckingApplicationStateSnapshot>()));
        AssertChanged(Snapshot(applications: new[] { Application(baseline: 0.7f) }));
        AssertChanged(Snapshot(applications: new[] { Application(isDucked: false) }));
        AssertChanged(Snapshot(applications: new[]
        {
            Application(statuses: new[] { RestorationStatus.RestorePending })
        }));
    }

    [Fact]
    public void Identity_removal_is_detected()
    {
        var detector = new DuckingStateSnapshotChangeDetector();
        var twoApplications = Snapshot(applications: new[]
        {
            Application(),
            new DuckingApplicationStateSnapshot(
                new ApplicationAudioIdentity(Endpoint, @"C:\Games\Other\other.exe"),
                0.6f,
                true,
                new[] { RestorationStatus.Ducked })
        });

        Assert.True(detector.ShouldLog(twoApplications));
        Assert.True(detector.ShouldLog(Snapshot()));
    }

    [Fact]
    public void Identity_addition_is_detected()
    {
        var detector = new DuckingStateSnapshotChangeDetector();
        var twoApplications = Snapshot(applications: new[]
        {
            Application(),
            new DuckingApplicationStateSnapshot(
                new ApplicationAudioIdentity(Endpoint, @"C:\Games\Other\other.exe"),
                0.6f,
                true,
                new[] { RestorationStatus.Ducked })
        });

        Assert.True(detector.ShouldLog(Snapshot()));
        Assert.True(detector.ShouldLog(twoApplications));
    }

    [Fact]
    public void Trigger_and_application_order_do_not_change_snapshot()
    {
        var first = Snapshot(
            triggers: new[] { "Discord.exe", "DiscordCanary.exe" },
            applications: new[]
            {
                Application(),
                new DuckingApplicationStateSnapshot(
                    new ApplicationAudioIdentity(Endpoint, @"C:\Games\Other\other.exe"),
                    0.6f,
                    false,
                    new[] { RestorationStatus.RestorePending })
            });
        var second = Snapshot(
            triggers: new[] { "discordcanary.exe", "DISCORD.EXE" },
            applications: first.Applications.Reverse());
        var detector = new DuckingStateSnapshotChangeDetector();

        Assert.True(detector.ShouldLog(first));
        Assert.False(detector.ShouldLog(second));
    }

    [Fact]
    public void Endpoint_change_is_detected_even_when_no_application_is_tracked()
    {
        var detector = new DuckingStateSnapshotChangeDetector();

        Assert.True(detector.ShouldLog(Snapshot(
            endpoint: Endpoint,
            applications: Array.Empty<DuckingApplicationStateSnapshot>())));
        Assert.True(detector.ShouldLog(Snapshot(
            endpoint: "endpoint-b",
            applications: Array.Empty<DuckingApplicationStateSnapshot>())));
    }

    [Fact]
    public void Restoration_status_order_does_not_change_snapshot()
    {
        var detector = new DuckingStateSnapshotChangeDetector();

        Assert.True(detector.ShouldLog(Snapshot(applications: new[]
        {
            Application(statuses: new[]
            {
                RestorationStatus.RestorePending,
                RestorationStatus.Ducked
            })
        })));
        Assert.False(detector.ShouldLog(Snapshot(applications: new[]
        {
            Application(statuses: new[]
            {
                RestorationStatus.Ducked,
                RestorationStatus.RestorePending
            })
        })));
    }

    [Fact]
    public void Fresh_detector_always_logs_first_snapshot()
    {
        Assert.True(new DuckingStateSnapshotChangeDetector().ShouldLog(Snapshot()));
        Assert.True(new DuckingStateSnapshotChangeDetector().ShouldLog(Snapshot()));
    }

    [Fact]
    public void Snapshot_copies_caller_owned_collections()
    {
        var triggers = new List<string> { "Discord.exe" };
        var statuses = new List<RestorationStatus> { RestorationStatus.Ducked };
        var applications = new List<DuckingApplicationStateSnapshot>
        {
            Application(statuses: statuses)
        };
        var snapshot = Snapshot(triggers: triggers, applications: applications);

        triggers.Add("DiscordCanary.exe");
        statuses.Clear();
        applications.Clear();

        Assert.Equal(new[] { "Discord.exe" }, snapshot.ActiveTriggers);
        Assert.Single(snapshot.Applications);
        Assert.Equal(
            new[] { RestorationStatus.Ducked },
            snapshot.Applications[0].RestorationStatuses);
    }

    [Fact]
    public void Capture_uses_last_operation_observation_without_additional_port_calls()
    {
        var repository = new CountingObligationRepository();
        var selector = new CountingEndpointSelector();
        var stateStore = new ApplicationVolumeStateStore();
        var service = new VolumeDuckingService(
            new SuccessfulVolumeWriter(),
            new DuckingSessionClassifier(),
            stateStore,
            repository,
            selector);
        var settings = new VoiceDuckSettings(
            new DuckingPolicy(0.5, 10),
            new[] { new TriggerApp("Discord.exe") },
            Array.Empty<ExcludeApp>());
        var session = new AudioSessionInfo(
            new AudioSessionIdentity(200, "ggst.exe", Endpoint, "session-1"),
            0.8f,
            false,
            Path);

        service.ApplyDucking(new[] { session }, settings, "VoiceDuck.exe");
        var selectorCalls = selector.CallCount;
        var loadCalls = repository.LoadCount;
        var saveCalls = repository.SaveCount;

        var snapshot = service.CaptureStateSnapshot(
            DuckingPhase.Ducking,
            new[] { "Discord.exe" });

        Assert.Equal(Endpoint, snapshot.SelectedEndpointId);
        Assert.Single(snapshot.Applications);
        Assert.Equal(
            new[] { RestorationStatus.Ducked },
            snapshot.Applications[0].RestorationStatuses);
        Assert.Equal(selectorCalls, selector.CallCount);
        Assert.Equal(loadCalls, repository.LoadCount);
        Assert.Equal(saveCalls, repository.SaveCount);
    }

    private static void AssertChanged(DuckingStateSnapshot changed)
    {
        var detector = new DuckingStateSnapshotChangeDetector();
        Assert.True(detector.ShouldLog(Snapshot()));
        Assert.True(detector.ShouldLog(changed));
    }

    private static DuckingStateSnapshot Snapshot(
        DuckingPhase phase = DuckingPhase.Ducking,
        string? endpoint = Endpoint,
        IEnumerable<string>? triggers = null,
        IEnumerable<DuckingApplicationStateSnapshot>? applications = null)
    {
        return new DuckingStateSnapshot(
            phase,
            endpoint,
            triggers ?? new[] { "Discord.exe" },
            applications ?? new[] { Application() });
    }

    private static DuckingApplicationStateSnapshot Application(
        float baseline = 0.8f,
        bool isDucked = true,
        IEnumerable<RestorationStatus>? statuses = null)
    {
        return new DuckingApplicationStateSnapshot(
            new ApplicationAudioIdentity(Endpoint, Path),
            baseline,
            isDucked,
            statuses ?? new[] { RestorationStatus.Ducked });
    }

    private sealed class SuccessfulVolumeWriter : IAudioSessionVolumeWriter
    {
        public VolumeWriteResult SetVolume(AudioSessionIdentity identity, float volume)
        {
            return VolumeWriteResult.Succeeded;
        }
    }

    private sealed class CountingEndpointSelector : IAudioEndpointSelector
    {
        public int CallCount { get; private set; }

        public string? GetDefaultMultimediaEndpointId()
        {
            CallCount++;
            return Endpoint;
        }
    }

    private sealed class CountingObligationRepository : IRestorationObligationRepository
    {
        private IReadOnlyList<RestorationObligation> _obligations =
            Array.Empty<RestorationObligation>();

        public int LoadCount { get; private set; }
        public int SaveCount { get; private set; }

        public RestorationObligationLoadResult LoadAll()
        {
            LoadCount++;
            return new RestorationObligationLoadResult(_obligations, false);
        }

        public void SaveAll(IReadOnlyList<RestorationObligation> obligations)
        {
            SaveCount++;
            _obligations = obligations.ToArray();
        }

        public void DeleteAll()
        {
            _obligations = Array.Empty<RestorationObligation>();
        }
    }
}
