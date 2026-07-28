namespace VoiceDuck.Core.Tests;

public class Issue91MultiDeviceRegressionTests
{
    private const string DefaultDevice = "default-device";
    private const string GgstPath = @"C:\Games\GGST.exe";
    private const string SystemLikePath = @"C:\Apps\SystemService.exe";
    private const string ConflictPath = @"C:\Games\Conflict.exe";
    private const string NeighborPath = @"C:\Games\Neighbor.exe";

    private static readonly string[] StaleDevices =
    {
        "stale-device-1",
        "stale-device-2",
        "stale-device-3",
        "stale-device-4",
        "stale-device-5",
    };

    private static readonly VoiceDuckSettings Settings = new(
        new DuckingPolicy(0.5, 10),
        new[] { new TriggerApp("Discord.exe") },
        new[] { new ExcludeApp("Excluded.exe") });

    [Fact]
    public void Six_endpoint_incident_lifecycle_restores_only_default_baseline()
    {
        var fixture = new Fixture();

        fixture.Service.ApplyDucking(SixEndpointGgstSessions(), Settings, "VoiceDuck.exe");

        var defaultIdentity = new ApplicationAudioIdentity(DefaultDevice, GgstPath);
        var state = Assert.Single(fixture.Store.GetAll());
        Assert.Equal(defaultIdentity, state.Identity);
        Assert.Equal(1.0f, state.BaselineVolume);
        Assert.True(state.IsDucked);
        var ducked = Assert.Single(fixture.Repository.Existing);
        Assert.Equal(defaultIdentity, ducked.Identity);
        Assert.Equal(1.0f, ducked.BaselineVolume);
        Assert.Equal(RestorationStatus.Ducked, ducked.Status);
        AssertExactWrite(fixture.Writer.Calls, DefaultDevice, 100, 0.5f);
        AssertNoStaleEndpointEffects(fixture);

        fixture.Writer.Calls.Clear();
        fixture.Service.RestoreVolumes(Array.Empty<AudioSessionInfo>());

        Assert.Empty(fixture.Writer.Calls);
        var pending = Assert.Single(fixture.Repository.Existing);
        Assert.Equal(defaultIdentity, pending.Identity);
        Assert.Equal(1.0f, pending.BaselineVolume);
        Assert.Equal(RestorationStatus.RestorePending, pending.Status);
        Assert.Single(fixture.Store.GetAll());
        AssertNoStaleEndpointEffects(fixture);

        fixture.Service.ApplyDeferredRestores(SixEndpointGgstSessions(defaultVolume: 0.5f, pidOffset: 1000));

        AssertExactWrite(fixture.Writer.Calls, DefaultDevice, 1100, 1.0f);
        Assert.Empty(fixture.Store.GetAll());
        Assert.Empty(fixture.Repository.Existing);
        AssertNoStaleEndpointEffects(fixture);
    }

    [Fact]
    public void Repeated_trigger_and_fresh_process_preserve_one_point_zero_baseline()
    {
        var sharedRepository = new Repository();
        var firstProcess = new Fixture(sharedRepository);

        firstProcess.Service.ApplyDucking(SixEndpointGgstSessions(), Settings, "VoiceDuck.exe");
        firstProcess.Writer.Calls.Clear();
        firstProcess.Service.ApplyDucking(
            SixEndpointGgstSessions(defaultVolume: 0.5f, pidOffset: 100),
            Settings,
            "VoiceDuck.exe");

        Assert.Equal(1.0f, Assert.Single(firstProcess.Store.GetAll()).BaselineVolume);
        Assert.Equal(1.0f, Assert.Single(sharedRepository.Existing).BaselineVolume);
        AssertExactWrite(firstProcess.Writer.Calls, DefaultDevice, 200, 0.5f);
        Assert.DoesNotContain(firstProcess.Writer.Calls, call => NearlyEqual(call.Volume, 0.25f));
        AssertNoStaleEndpointEffects(firstProcess);

        var restartedProcess = new Fixture(sharedRepository);
        var recovery = restartedProcess.Service.LoadAndPopulateStartupState();

        Assert.Equal(1, recovery.LoadedCount);
        Assert.Equal(1.0f, Assert.Single(restartedProcess.Store.GetAll()).BaselineVolume);
        Assert.Equal(RestorationStatus.RestorePending, Assert.Single(sharedRepository.Existing).Status);

        restartedProcess.Service.ApplyDucking(
            SixEndpointGgstSessions(defaultVolume: 0.5f, pidOffset: 200),
            Settings,
            "VoiceDuck.exe");

        Assert.Equal(1.0f, Assert.Single(restartedProcess.Store.GetAll()).BaselineVolume);
        Assert.Equal(1.0f, Assert.Single(sharedRepository.Existing).BaselineVolume);
        AssertExactWrite(restartedProcess.Writer.Calls, DefaultDevice, 300, 0.5f);
        Assert.DoesNotContain(restartedProcess.Writer.Calls, call => NearlyEqual(call.Volume, 0.25f));
        Assert.Contains(
            restartedProcess.Logger.Messages,
            message => message.Contains("reason=existing_obligation baseline=1", StringComparison.Ordinal));

        restartedProcess.Writer.Calls.Clear();
        restartedProcess.Service.RestoreVolumes(
            SixEndpointGgstSessions(defaultVolume: 0.5f, pidOffset: 200));

        AssertExactWrite(restartedProcess.Writer.Calls, DefaultDevice, 300, 1.0f);
        Assert.Empty(restartedProcess.Store.GetAll());
        Assert.Empty(sharedRepository.Existing);
        AssertNoStaleEndpointEffects(restartedProcess);
    }

    [Fact]
    public void General_eligibility_rejects_degraded_sessions_but_not_a_system_like_name()
    {
        var fixture = new Fixture();
        var sessions = new[]
        {
            Session(0, "PidZero.exe", DefaultDevice, 1.0f, @"C:\Apps\pid-zero.exe"),
            Session(201, "Unresolved.exe", DefaultDevice, 1.0f, @"C:\Apps\unresolved.exe", instance: ""),
            Session(202, " ", DefaultDevice, 1.0f, @"C:\Apps\missing-name.exe"),
            Session(203, "MissingPath.exe", DefaultDevice, 1.0f, " "),
            Session(204, "WrongEndpoint.exe", "other-device", 1.0f, @"C:\Apps\wrong-endpoint.exe"),
            Session(205, "Discord.exe", DefaultDevice, 1.0f, @"C:\Apps\discord.exe"),
            Session(206, "VoiceDuck.exe", DefaultDevice, 1.0f, @"C:\Apps\voiceduck.exe"),
            Session(207, "Excluded.exe", DefaultDevice, 1.0f, @"C:\Apps\excluded.exe"),
            Session(208, "SystemService.exe", DefaultDevice, 1.0f, SystemLikePath),
        };

        fixture.Service.ApplyDucking(sessions, Settings, "VoiceDuck.exe");
        var rejectionCount = EligibilityLogs(fixture).Count;
        fixture.Service.ApplyDucking(sessions, Settings, "VoiceDuck.exe");

        var eligibleIdentity = new ApplicationAudioIdentity(DefaultDevice, SystemLikePath);
        var state = Assert.Single(fixture.Store.GetAll());
        Assert.Equal(eligibleIdentity, state.Identity);
        Assert.Equal(1.0f, state.BaselineVolume);
        var obligation = Assert.Single(fixture.Repository.Existing);
        Assert.Equal(eligibleIdentity, obligation.Identity);
        Assert.All(fixture.Writer.Calls, call =>
        {
            Assert.Equal((uint)208, call.Identity.ProcessId);
            Assert.Equal(DefaultDevice, call.Identity.RenderDeviceId);
            Assert.True(NearlyEqual(call.Volume, 0.5f));
        });
        Assert.Equal(2, fixture.Writer.Calls.Count);

        var eligibilityLogs = EligibilityLogs(fixture);
        Assert.Equal(8, rejectionCount);
        Assert.Equal(rejectionCount, eligibilityLogs.Count);
        AssertReasons(
            eligibilityLogs,
            ControlEligibilityRejectionReason.InvalidProcessId,
            ControlEligibilityRejectionReason.UnresolvedIdentity,
            ControlEligibilityRejectionReason.MissingProcessName,
            ControlEligibilityRejectionReason.MissingExecutablePath,
            ControlEligibilityRejectionReason.IrrelevantEndpoint,
            ControlEligibilityRejectionReason.TriggerApplication,
            ControlEligibilityRejectionReason.Self,
            ControlEligibilityRejectionReason.UserExcluded);
        Assert.DoesNotContain(
            eligibilityLogs,
            message => message.Contains("name=SystemService.exe", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Endpoint_lookup_failure_is_deduplicated_and_has_no_effects(bool throws)
    {
        var fixture = new Fixture();
        fixture.Selector.EndpointId = null;
        fixture.Selector.Throw = throws;
        var sessions = SixEndpointGgstSessions();

        fixture.Service.ApplyDucking(sessions, Settings, "VoiceDuck.exe");
        fixture.Service.ApplyDucking(sessions, Settings, "VoiceDuck.exe");

        Assert.Empty(fixture.Store.GetAll());
        Assert.Empty(fixture.Repository.Existing);
        Assert.Equal(0, fixture.Repository.SaveCount);
        Assert.Empty(fixture.Writer.Calls);
        var endpointLog = Assert.Single(fixture.Logger.Messages.Where(message =>
            message.StartsWith("EndpointSelection:", StringComparison.Ordinal)));
        Assert.Contains("reason=lookup_failed", endpointLog);
    }

    [Fact]
    public void Same_default_identity_conflict_is_rejected_without_blocking_neighbor()
    {
        var fixture = new Fixture();
        var sessions = new[]
        {
            Session(301, "Conflict.exe", DefaultDevice, 1.0f, ConflictPath),
            Session(302, "Conflict.exe", DefaultDevice, 0.5f, ConflictPath),
            Session(303, "Neighbor.exe", DefaultDevice, 0.8f, NeighborPath),
        };

        fixture.Service.ApplyDucking(sessions, Settings, "VoiceDuck.exe");
        fixture.Service.ApplyDucking(sessions, Settings, "VoiceDuck.exe");

        var neighborIdentity = new ApplicationAudioIdentity(DefaultDevice, NeighborPath);
        var state = Assert.Single(fixture.Store.GetAll());
        Assert.Equal(neighborIdentity, state.Identity);
        Assert.Equal(0.8f, state.BaselineVolume);
        Assert.All(fixture.Repository.Existing, obligation =>
            Assert.Equal(neighborIdentity, obligation.Identity));
        Assert.Equal(2, fixture.Writer.Calls.Count);
        Assert.All(fixture.Writer.Calls, call =>
        {
            Assert.Equal((uint)303, call.Identity.ProcessId);
            Assert.True(NearlyEqual(call.Volume, 0.4f));
        });
        Assert.DoesNotContain(
            fixture.Repository.Existing,
            obligation => string.Equals(
                obligation.Identity.ExecutablePath,
                ConflictPath,
                StringComparison.OrdinalIgnoreCase));
        var conflictLog = Assert.Single(fixture.Logger.Messages.Where(message =>
            message.StartsWith(
                $"BaselineDecision: identity={DefaultDevice}|{ConflictPath}",
                StringComparison.Ordinal)
            && message.Contains(
                "outcome=rejected reason=volume_conflict",
                StringComparison.Ordinal)));
        Assert.Contains("candidates=[0.5,1]", conflictLog);
    }

    private static AudioSessionInfo[] SixEndpointGgstSessions(
        float defaultVolume = 1.0f,
        uint pidOffset = 0)
    {
        var sessions = new List<AudioSessionInfo>
        {
            Session(100 + pidOffset, "GGST.exe", DefaultDevice, defaultVolume, GgstPath),
        };

        for (var index = 0; index < StaleDevices.Length; index++)
        {
            sessions.Add(Session(
                (uint)(101 + index) + pidOffset,
                "GGST.exe",
                StaleDevices[index],
                0.5f,
                GgstPath));
        }

        return sessions.ToArray();
    }

    private static AudioSessionInfo Session(
        uint pid,
        string processName,
        string device,
        float volume,
        string? path,
        string? instance = null)
    {
        return new AudioSessionInfo(
            new AudioSessionIdentity(
                pid,
                processName,
                device,
                instance ?? $"instance-{pid}"),
            volume,
            false,
            path);
    }

    private static void AssertExactWrite(
        IReadOnlyList<WriteCall> calls,
        string device,
        uint pid,
        float volume)
    {
        var call = Assert.Single(calls);
        Assert.Equal(device, call.Identity.RenderDeviceId);
        Assert.Equal(pid, call.Identity.ProcessId);
        Assert.True(
            NearlyEqual(call.Volume, volume),
            $"Expected volume {volume}, actual {call.Volume}");
    }

    private static void AssertNoStaleEndpointEffects(Fixture fixture)
    {
        foreach (var staleDevice in StaleDevices)
        {
            Assert.DoesNotContain(
                fixture.Writer.Calls,
                call => string.Equals(
                    call.Identity.RenderDeviceId,
                    staleDevice,
                    StringComparison.Ordinal));
            Assert.DoesNotContain(
                fixture.Store.GetAll(),
                state => string.Equals(
                    state.Identity.RenderDeviceId,
                    staleDevice,
                    StringComparison.Ordinal));
            Assert.DoesNotContain(
                fixture.Repository.Existing,
                obligation => string.Equals(
                    obligation.Identity.RenderDeviceId,
                    staleDevice,
                    StringComparison.Ordinal));
        }
    }

    private static List<string> EligibilityLogs(Fixture fixture) =>
        fixture.Logger.Messages
            .Where(message => message.StartsWith("ControlEligibility:", StringComparison.Ordinal))
            .ToList();

    private static void AssertReasons(
        IReadOnlyList<string> messages,
        params ControlEligibilityRejectionReason[] reasons)
    {
        foreach (var reason in reasons)
        {
            Assert.Contains(
                messages,
                message => message.Contains($"reason={reason}", StringComparison.Ordinal));
        }
    }

    private static bool NearlyEqual(float actual, float expected) =>
        Math.Abs(actual - expected) < 0.0001f;

    private sealed class Fixture
    {
        public Writer Writer { get; } = new();
        public ApplicationVolumeStateStore Store { get; } = new();
        public Repository Repository { get; }
        public Selector Selector { get; } = new();
        public Logger Logger { get; } = new();
        public VolumeDuckingService Service { get; }

        public Fixture(Repository? repository = null)
        {
            Repository = repository ?? new Repository();
            Service = new VolumeDuckingService(
                Writer,
                new DuckingSessionClassifier(),
                Store,
                Repository,
                Selector,
                Logger);
        }
    }

    private sealed record WriteCall(AudioSessionIdentity Identity, float Volume);

    private sealed class Writer : IAudioSessionVolumeWriter
    {
        public List<WriteCall> Calls { get; } = new();

        public VolumeWriteResult SetVolume(AudioSessionIdentity identity, float volume)
        {
            Calls.Add(new WriteCall(identity, volume));
            return VolumeWriteResult.Succeeded;
        }
    }

    private sealed class Repository : IRestorationObligationRepository
    {
        public List<RestorationObligation> Existing { get; private set; } = new();
        public int SaveCount { get; private set; }

        public RestorationObligationLoadResult LoadAll() =>
            new(Existing.ToArray(), WasCorrupt: false);

        public void SaveAll(IReadOnlyList<RestorationObligation> obligations)
        {
            Existing = obligations.ToList();
            SaveCount++;
        }

        public void DeleteAll() => Existing.Clear();
    }

    private sealed class Selector : IAudioEndpointSelector
    {
        public string? EndpointId { get; set; } = DefaultDevice;
        public bool Throw { get; set; }

        public string? GetDefaultMultimediaEndpointId()
        {
            if (Throw)
                throw new InvalidOperationException("selector failure");
            return EndpointId;
        }
    }

    private sealed class Logger : ILogger
    {
        public List<string> Messages { get; } = new();

        public void Info(string message) => Messages.Add(message);
        public void Warn(string message) => Messages.Add(message);
        public void Error(string message) => Messages.Add(message);
    }
}
