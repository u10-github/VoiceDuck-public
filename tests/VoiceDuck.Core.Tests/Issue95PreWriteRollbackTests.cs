namespace VoiceDuck.Core.Tests;

public class Issue95PreWriteRollbackTests
{
    private const string Device = "default-device";
    private const string PathA = @"C:\Apps\a.exe";
    private const string PathB = @"C:\Apps\b.exe";
    private const string PathC = @"C:\Apps\c.exe";
    private const string PathD = @"C:\Apps\d.exe";
    private const string PathE = @"C:\Apps\e.exe";

    private static readonly VoiceDuckSettings Settings = new(
        new DuckingPolicy(0.5, 10),
        new[] { new TriggerApp("Discord.exe") },
        Array.Empty<ExcludeApp>());

    [Fact]
    public void Existing_equal_baseline_is_rolled_back_when_prewrite_save_throws()
    {
        var fixture = new Fixture();
        var identity = AppIdentity(PathA);
        fixture.Store.Add(new ApplicationVolumeState(identity, 0.8f, isDucked: false));
        fixture.Repository.Existing.Add(Obligation(identity, 0.8f));
        fixture.Repository.FailureMode = SaveFailureMode.BeforeCommit;

        fixture.Service.ApplyDucking(
            new[] { Session(1, PathA, 0.8f) },
            Settings,
            "VoiceDuck.exe");

        Assert.Equal(1, fixture.Store.Count);
        Assert.True(fixture.Store.TryGet(identity, out var state));
        Assert.Equal(0.8f, state!.BaselineVolume);
        Assert.False(state.IsDucked);
        Assert.Empty(fixture.Writer.Calls);
        Assert.Single(fixture.Repository.Existing, obligation =>
            obligation.Identity.Equals(identity)
            && obligation.BaselineVolume == 0.8f);
        Assert.Equal(1, fixture.Repository.SaveAttempts);
        Assert.Contains(fixture.Logger.Messages, message =>
            message.Contains("DuckPersistPreWrite:", StringComparison.Ordinal)
            && message.Contains(identity.ToString(), StringComparison.Ordinal));
    }

    [Fact]
    public void Differing_durable_baseline_rolls_back_then_retry_reloads_authority_before_write()
    {
        var fixture = new Fixture();
        var identity = AppIdentity(PathA);
        fixture.Store.Add(new ApplicationVolumeState(identity, 0.8f, isDucked: false));
        fixture.Repository.Existing.Add(Obligation(identity, 1.0f));
        fixture.Repository.FailureMode = SaveFailureMode.BeforeCommit;

        fixture.Service.ApplyDucking(
            new[] { Session(1, PathA, 0.8f) },
            Settings,
            "VoiceDuck.exe");

        Assert.True(fixture.Store.TryGet(identity, out var rolledBack));
        Assert.Equal(0.8f, rolledBack!.BaselineVolume);
        Assert.False(rolledBack.IsDucked);
        Assert.Empty(fixture.Writer.Calls);
        Assert.Single(fixture.Repository.Existing, obligation =>
            obligation.Identity.Equals(identity)
            && obligation.BaselineVolume == 1.0f);

        fixture.Events.Clear();
        fixture.Repository.FailureMode = SaveFailureMode.None;
        fixture.Service.ApplyDucking(
            new[] { Session(1, PathA, 0.8f) },
            Settings,
            "VoiceDuck.exe");

        Assert.True(fixture.Store.TryGet(identity, out var retried));
        Assert.Equal(1.0f, retried!.BaselineVolume);
        Assert.True(retried.IsDucked);
        var write = Assert.Single(fixture.Writer.Calls);
        Assert.Equal(0.5f, write.Volume);
        Assert.Equal("save", fixture.Events[0]);
        Assert.Equal("write", fixture.Events[1]);
        Assert.Single(fixture.Repository.Existing, obligation =>
            obligation.Identity.Equals(identity)
            && obligation.BaselineVolume == 1.0f
            && obligation.Status == RestorationStatus.Ducked);
    }

    [Fact]
    public void Commit_then_throw_rolls_back_memory_and_retry_uses_committed_durable_state()
    {
        var fixture = new Fixture();
        var identity = AppIdentity(PathA);
        fixture.Store.Add(new ApplicationVolumeState(identity, 0.8f, isDucked: false));
        fixture.Repository.Existing.Add(Obligation(identity, 1.0f));
        fixture.Repository.FailureMode = SaveFailureMode.AfterCommit;

        fixture.Service.ApplyDucking(
            new[] { Session(1, PathA, 0.8f) },
            Settings,
            "VoiceDuck.exe");

        Assert.True(fixture.Store.TryGet(identity, out var rolledBack));
        Assert.Equal(0.8f, rolledBack!.BaselineVolume);
        Assert.False(rolledBack.IsDucked);
        Assert.Empty(fixture.Writer.Calls);
        Assert.Single(fixture.Repository.Existing, obligation =>
            obligation.Identity.Equals(identity)
            && obligation.BaselineVolume == 1.0f
            && obligation.Status == RestorationStatus.RestorePending);

        fixture.Events.Clear();
        fixture.Repository.FailureMode = SaveFailureMode.None;
        fixture.Service.ApplyDucking(
            new[] { Session(1, PathA, 0.8f) },
            Settings,
            "VoiceDuck.exe");

        Assert.True(fixture.Store.TryGet(identity, out var retried));
        Assert.Equal(1.0f, retried!.BaselineVolume);
        Assert.True(retried.IsDucked);
        var write = Assert.Single(fixture.Writer.Calls);
        Assert.Equal(0.5f, write.Volume);
        Assert.Equal("save", fixture.Events[0]);
        Assert.Equal("write", fixture.Events[1]);
    }

    [Fact]
    public void Mixed_batch_rolls_back_existing_removes_new_and_logs_only_accepted_identities()
    {
        var fixture = new Fixture();
        var existingA = AppIdentity(PathA);
        var existingB = AppIdentity(PathB);
        var newlyTracked = AppIdentity(PathC);
        var unrelated = AppIdentity(PathD);
        var duplicate = AppIdentity(PathE);
        fixture.Store.Add(new ApplicationVolumeState(existingA, 0.8f, isDucked: false));
        fixture.Store.Add(new ApplicationVolumeState(existingB, 0.6f, isDucked: false));
        fixture.Store.Add(new ApplicationVolumeState(unrelated, 0.7f, isDucked: false));
        fixture.Store.Add(new ApplicationVolumeState(duplicate, 0.9f, isDucked: false));
        fixture.Repository.Existing.Add(Obligation(existingA, 1.0f));
        fixture.Repository.Existing.Add(Obligation(existingB, 0.6f));
        fixture.Repository.Existing.Add(Obligation(duplicate, 0.9f));
        fixture.Repository.Existing.Add(Obligation(duplicate, 0.9f));
        fixture.Repository.FailureMode = SaveFailureMode.BeforeCommit;

        fixture.Service.ApplyDucking(
            new[]
            {
                Session(1, PathA, 0.8f),
                Session(2, PathB, 0.6f),
                Session(3, PathC, 0.9f),
                Session(5, PathE, 0.9f),
            },
            Settings,
            "VoiceDuck.exe");

        AssertState(fixture.Store, existingA, 0.8f, isDucked: false);
        AssertState(fixture.Store, existingB, 0.6f, isDucked: false);
        Assert.False(fixture.Store.TryGet(newlyTracked, out _));
        AssertState(fixture.Store, unrelated, 0.7f, isDucked: false);
        AssertState(fixture.Store, duplicate, 0.9f, isDucked: false);
        Assert.Empty(fixture.Writer.Calls);

        var failureLogs = fixture.Logger.Messages
            .Where(message => message.Contains("DuckPersistPreWrite:", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(3, failureLogs.Count);
        Assert.Contains(failureLogs, message => message.Contains(existingA.ToString(), StringComparison.Ordinal));
        Assert.Contains(failureLogs, message => message.Contains(existingB.ToString(), StringComparison.Ordinal));
        Assert.Contains(failureLogs, message => message.Contains(newlyTracked.ToString(), StringComparison.Ordinal));
        Assert.DoesNotContain(failureLogs, message => message.Contains(unrelated.ToString(), StringComparison.Ordinal));
        Assert.DoesNotContain(failureLogs, message => message.Contains(duplicate.ToString(), StringComparison.Ordinal));
    }

    private static void AssertState(
        ApplicationVolumeStateStore store,
        ApplicationAudioIdentity identity,
        float baseline,
        bool isDucked)
    {
        Assert.True(store.TryGet(identity, out var state));
        Assert.Equal(baseline, state!.BaselineVolume);
        Assert.Equal(isDucked, state.IsDucked);
    }

    private static ApplicationAudioIdentity AppIdentity(string path) => new(Device, path);

    private static AudioSessionInfo Session(uint processId, string path, float volume) =>
        new(
            new AudioSessionIdentity(processId, $"app-{processId}.exe", Device, $"inst-{processId}"),
            volume,
            IsMuted: false,
            path);

    private static RestorationObligation Obligation(
        ApplicationAudioIdentity identity,
        float baseline) =>
        new(
            identity,
            baseline,
            RestorationStatus.Ducked,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

    private enum SaveFailureMode
    {
        None,
        BeforeCommit,
        AfterCommit,
    }

    private sealed class Fixture
    {
        public List<string> Events { get; } = new();
        public Writer Writer { get; }
        public Repository Repository { get; }
        public ApplicationVolumeStateStore Store { get; } = new();
        public TestLogger Logger { get; } = new();
        public VolumeDuckingService Service { get; }

        public Fixture()
        {
            Writer = new Writer(Events);
            Repository = new Repository(Events);
            Service = new VolumeDuckingService(
                Writer,
                new DuckingSessionClassifier(),
                Store,
                Repository,
                new EndpointSelector(),
                Logger);
        }
    }

    private sealed class Writer : IAudioSessionVolumeWriter
    {
        private readonly List<string> _events;

        public Writer(List<string> events)
        {
            _events = events;
        }

        public List<(AudioSessionIdentity Identity, float Volume)> Calls { get; } = new();

        public VolumeWriteResult SetVolume(AudioSessionIdentity identity, float volume)
        {
            _events.Add("write");
            Calls.Add((identity, volume));
            return VolumeWriteResult.Succeeded;
        }
    }

    private sealed class Repository : IRestorationObligationRepository
    {
        private readonly List<string> _events;

        public Repository(List<string> events)
        {
            _events = events;
        }

        public List<RestorationObligation> Existing { get; private set; } = new();
        public SaveFailureMode FailureMode { get; set; }
        public int SaveAttempts { get; private set; }

        public RestorationObligationLoadResult LoadAll() =>
            new(Existing.ToArray(), WasCorrupt: false);

        public void SaveAll(IReadOnlyList<RestorationObligation> obligations)
        {
            SaveAttempts++;
            _events.Add("save");

            if (FailureMode == SaveFailureMode.BeforeCommit)
                throw new InvalidOperationException("save failed before commit");

            Existing = obligations.ToList();
            if (FailureMode == SaveFailureMode.AfterCommit)
                throw new InvalidOperationException("save failed after commit");
        }

        public void DeleteAll() => Existing.Clear();
    }

    private sealed class EndpointSelector : IAudioEndpointSelector
    {
        public string? GetDefaultMultimediaEndpointId() => Device;
    }

    private sealed class TestLogger : ILogger
    {
        public List<string> Messages { get; } = new();

        public void Info(string message) => Messages.Add(message);

        public void Warn(string message) => Messages.Add(message);

        public void Error(string message) => Messages.Add(message);
    }
}
