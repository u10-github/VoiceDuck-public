namespace VoiceDuck.Core.Tests;

public class VolumeDuckingServiceTests
{
    private const string DefaultDevice = "default-device";
    private const string DefaultExePath = @"C:\Program Files\App\app.exe";

    private static readonly VoiceDuckSettings DefaultSettings = new(
        new DuckingPolicy(0.5, 10),
        new[] { new TriggerApp("Discord.exe") },
        Array.Empty<ExcludeApp>());

    private static readonly VoiceDuckSettings Ratio100 = new(
        new DuckingPolicy(1.0, 10),
        new[] { new TriggerApp("Discord.exe") },
        Array.Empty<ExcludeApp>());

    private static readonly VoiceDuckSettings NoTriggerSettings = new(
        new DuckingPolicy(0.5, 10),
        Array.Empty<TriggerApp>(),
        Array.Empty<ExcludeApp>());

    private static AudioSessionIdentity SessionId(uint pid, string name, string device = DefaultDevice) =>
        new(pid, name, device, $"inst-{pid}");

    private static AudioSessionIdentity IdentityWithInst(uint pid, string name, string inst, string device = DefaultDevice) =>
        new(pid, name, device, inst);

    private static AudioSessionInfo Session(uint pid, string name, float vol = 0.8f, string? path = DefaultExePath) =>
        new(SessionId(pid, name), vol, false, path);

    private static readonly ApplicationAudioIdentity AppKey =
        new(DefaultDevice, DefaultExePath);

    private sealed class VolumeWriterMock : IAudioSessionVolumeWriter
    {
        public List<(AudioSessionIdentity identity, float volume)> Calls { get; } = new();
        public Func<AudioSessionIdentity, VolumeWriteResult>? ResultFor { get; set; }

        public VolumeWriteResult SetVolume(AudioSessionIdentity identity, float volume)
        {
            Calls.Add((identity, volume));
            return ResultFor?.Invoke(identity) ?? VolumeWriteResult.Succeeded;
        }
    }

    private sealed class ObligationRepoMock : IRestorationObligationRepository
    {
        public List<RestorationObligation> Saved { get; private set; } = new();
        public int SaveCount { get; set; }
        public bool ShouldThrowOnSave { get; set; }
        public bool WasCorrupt { get; set; }
        public List<RestorationObligation> Existing { get; set; } = new();

        public RestorationObligationLoadResult LoadAll()
        {
            return new RestorationObligationLoadResult(Existing.ToArray(), WasCorrupt);
        }

        public void SaveAll(IReadOnlyList<RestorationObligation> obligations)
        {
            if (ShouldThrowOnSave)
                throw new InvalidOperationException("mock save failure");
            Saved = new List<RestorationObligation>(obligations);
            Existing = new List<RestorationObligation>(obligations);
            SaveCount++;
        }

        public void DeleteAll()
        {
            Existing.Clear();
            Saved.Clear();
        }
    }

    private sealed class EndpointSelectorMock : IAudioEndpointSelector
    {
        public string? EndpointId { get; set; } = DefaultDevice;
        public string? GetDefaultMultimediaEndpointId() => EndpointId;
    }

    private sealed class LoggerMock : ILogger
    {
        public List<string> Messages { get; } = new();

        public void Info(string message) => Messages.Add(message);
        public void Warn(string message) => Messages.Add(message);
        public void Error(string message) => Messages.Add(message);
    }

    // ── ApplyDucking ──

    [Fact]
    public void ApplyDucking_creates_state_and_sets_ducked_volume()
    {
        var writer = new VolumeWriterMock();
        var store = new ApplicationVolumeStateStore();
        var classifier = new DuckingSessionClassifier();
        var obligations = new ObligationRepoMock();
        var service = new VolumeDuckingService(writer, classifier, store, obligations, new EndpointSelectorMock());

        var sessions = new[] { Session(200, "Chrome.exe", 0.8f) };
        service.ApplyDucking(sessions, DefaultSettings, "VoiceDuck.exe");

        Assert.True(store.TryGet(AppKey, out var state));
        Assert.Equal(0.8f, state!.BaselineVolume);
        Assert.True(state.IsDucked);
        Assert.Contains(writer.Calls, c =>
            c.identity.Equals(SessionId(200, "Chrome.exe")) &&
            Math.Abs(c.volume - 0.4f) < 0.001f);
    }

    [Fact]
    public void ApplyDucking_uses_existing_baseline()
    {
        var writer = new VolumeWriterMock();
        var store = new ApplicationVolumeStateStore();
        var classifier = new DuckingSessionClassifier();
        var obligations = new ObligationRepoMock();
        var service = new VolumeDuckingService(writer, classifier, store, obligations, new EndpointSelectorMock());

        // First pass: baseline 0.8, target 0.4
        service.ApplyDucking(new[] { Session(200, "Chrome.exe", 0.8f) },
            DefaultSettings, "VoiceDuck.exe");

        writer.Calls.Clear();

        // Second pass: current volume now 0.4, must NOT update baseline
        service.ApplyDucking(new[] { Session(200, "Chrome.exe", 0.4f) },
            DefaultSettings, "VoiceDuck.exe");

        Assert.True(store.TryGet(AppKey, out var state));
        Assert.Equal(0.8f, state!.BaselineVolume);
        // target still 0.8*0.5=0.4, not 0.4*0.5=0.2
        Assert.Contains(writer.Calls, c => Math.Abs(c.volume - 0.4f) < 0.001f);
    }

    [Fact]
    public void ApplyDucking_max_baseline_for_multi_session()
    {
        var writer = new VolumeWriterMock();
        var store = new ApplicationVolumeStateStore();
        var classifier = new DuckingSessionClassifier();
        var obligations = new ObligationRepoMock();
        var service = new VolumeDuckingService(writer, classifier, store, obligations, new EndpointSelectorMock());

        var sessions = new[]
        {
            Session(200, "Chrome.exe", 0.995f),
            Session(201, "Chrome.exe", 1.0f),
        };
        service.ApplyDucking(sessions, DefaultSettings, "VoiceDuck.exe");

        Assert.True(store.TryGet(AppKey, out var state));
        Assert.Equal(1.0f, state!.BaselineVolume);
        Assert.Equal(2, writer.Calls.Count);
        // Both sessions get target 1.0*0.5=0.5
        foreach (var call in writer.Calls)
            Assert.Equal(0.5f, call.volume, 3);
    }

    [Fact]
    public void ApplyDucking_different_devices_separate_states()
    {
        var writer = new VolumeWriterMock();
        var store = new ApplicationVolumeStateStore();
        var classifier = new DuckingSessionClassifier();
        var obligations = new ObligationRepoMock();
        var selector = new EndpointSelectorMock();
        var service = new VolumeDuckingService(writer, classifier, store, obligations, selector);

        selector.EndpointId = DefaultDevice;
        service.ApplyDucking(new[] { Session(200, "Chrome.exe", 1.0f, DefaultExePath) },
            DefaultSettings, "VoiceDuck.exe");

        selector.EndpointId = "device-B";
        service.ApplyDucking(
            new[] { new AudioSessionInfo(SessionId(200, "Chrome.exe", "device-B"), 0.3f, false, DefaultExePath) },
            DefaultSettings, "VoiceDuck.exe");

        Assert.Equal(2, store.Count);
        Assert.True(store.TryGet(new ApplicationAudioIdentity("device-B", DefaultExePath), out _));
    }

    [Fact]
    public void ApplyDucking_protects_trigger_apps()
    {
        var writer = new VolumeWriterMock();
        var store = new ApplicationVolumeStateStore();
        var classifier = new DuckingSessionClassifier();
        var obligations = new ObligationRepoMock();
        var service = new VolumeDuckingService(writer, classifier, store, obligations, new EndpointSelectorMock());

        var sessions = new[] { Session(100, "Discord.exe", 0.8f) };
        service.ApplyDucking(sessions, DefaultSettings, "VoiceDuck.exe");

        Assert.Empty(store.GetAll());
        Assert.Empty(writer.Calls);
    }

    [Fact]
    public void ApplyDucking_skips_unresolved()
    {
        var writer = new VolumeWriterMock();
        var store = new ApplicationVolumeStateStore();
        var classifier = new DuckingSessionClassifier();
        var obligations = new ObligationRepoMock();
        var service = new VolumeDuckingService(writer, classifier, store, obligations, new EndpointSelectorMock());

        var unresolved = new AudioSessionInfo(
            new AudioSessionIdentity(100, "Chrome.exe", "", ""), 0.8f, false, null);
        service.ApplyDucking(new[] { unresolved }, DefaultSettings, "VoiceDuck.exe");

        Assert.Empty(store.GetAll());
        Assert.Empty(writer.Calls);
    }

    [Fact]
    public void ApplyDucking_empty_sessions_does_nothing()
    {
        var writer = new VolumeWriterMock();
        var store = new ApplicationVolumeStateStore();
        var classifier = new DuckingSessionClassifier();
        var obligations = new ObligationRepoMock();
        var service = new VolumeDuckingService(writer, classifier, store, obligations, new EndpointSelectorMock());

        service.ApplyDucking(Array.Empty<AudioSessionInfo>(), DefaultSettings, "VoiceDuck.exe");

        Assert.Empty(store.GetAll());
        Assert.Empty(writer.Calls);
    }

    [Fact]
    public void ApplyDucking_ratio_1_0_sets_same_volume()
    {
        var writer = new VolumeWriterMock();
        var store = new ApplicationVolumeStateStore();
        var classifier = new DuckingSessionClassifier();
        var obligations = new ObligationRepoMock();
        var service = new VolumeDuckingService(writer, classifier, store, obligations, new EndpointSelectorMock());

        service.ApplyDucking(new[] { Session(200, "Chrome.exe", 0.8f) },
            Ratio100, "VoiceDuck.exe");

        Assert.Contains(writer.Calls, c => Math.Abs(c.volume - 0.8f) < 0.001f);
    }

    // ── ApplyDucking failure handling ──

    [Fact]
    public void ApplyDucking_write_failed_keeps_state()
    {
        var writer = new VolumeWriterMock();
        var store = new ApplicationVolumeStateStore();
        var classifier = new DuckingSessionClassifier();
        var obligations = new ObligationRepoMock();
        var service = new VolumeDuckingService(writer, classifier, store, obligations, new EndpointSelectorMock());

        var chromeId = SessionId(200, "Chrome.exe");
        writer.ResultFor = id => id.Equals(chromeId) ? VolumeWriteResult.Failed : VolumeWriteResult.Succeeded;

        service.ApplyDucking(new[] { Session(200, "Chrome.exe", 0.8f) },
            DefaultSettings, "VoiceDuck.exe");

        // State created with baseline even if write fails
        Assert.True(store.TryGet(AppKey, out var state));
        Assert.Equal(0.8f, state!.BaselineVolume);
    }

    [Fact]
    public void ApplyDucking_partial_failure_does_not_remove_state()
    {
        var writer = new VolumeWriterMock();
        var store = new ApplicationVolumeStateStore();
        var classifier = new DuckingSessionClassifier();
        var obligations = new ObligationRepoMock();
        var service = new VolumeDuckingService(writer, classifier, store, obligations, new EndpointSelectorMock());

        var sessions = new[]
        {
            new AudioSessionInfo(IdentityWithInst(200, "Chrome.exe", "inst-200"), 1.0f, false, DefaultExePath),
            new AudioSessionInfo(IdentityWithInst(200, "Chrome.exe", "inst-201"), 0.995f, false, DefaultExePath),
        };

        var inst200 = SessionId(200, "Chrome.exe", DefaultDevice);
        writer.ResultFor = id => id == inst200 ? VolumeWriteResult.Succeeded : VolumeWriteResult.Failed;

        service.ApplyDucking(sessions, DefaultSettings, "VoiceDuck.exe");

        Assert.True(store.TryGet(AppKey, out _));
    }

    // ── ApplyDucking persistence ──

    [Fact]
    public void ApplyDucking_persist_failure_does_not_set_volumes()
    {
        var writer = new VolumeWriterMock();
        var store = new ApplicationVolumeStateStore();
        var classifier = new DuckingSessionClassifier();
        var obligations = new ObligationRepoMock();
        obligations.ShouldThrowOnSave = true;
        var service = new VolumeDuckingService(writer, classifier, store, obligations, new EndpointSelectorMock());

        var sessions = new[] { Session(200, "Chrome.exe", 0.8f) };
        service.ApplyDucking(sessions, DefaultSettings, "VoiceDuck.exe");

        Assert.Empty(writer.Calls);
    }

    [Fact]
    public void ApplyDucking_persists_obligation_before_volume_write()
    {
        var writer = new VolumeWriterMock();
        var store = new ApplicationVolumeStateStore();
        var classifier = new DuckingSessionClassifier();
        var obligations = new ObligationRepoMock();
        var service = new VolumeDuckingService(writer, classifier, store, obligations, new EndpointSelectorMock());

        service.ApplyDucking(new[] { Session(200, "Chrome.exe", 0.8f) },
            DefaultSettings, "VoiceDuck.exe");

        Assert.Single(obligations.Saved);
        var obl = obligations.Saved[0];
        Assert.Equal(AppKey, obl.Identity);
        Assert.Equal(0.8f, obl.BaselineVolume, 3);
        Assert.Equal(RestorationStatus.Ducked, obl.Status);
        Assert.Equal(RestorationObligation.CurrentSchemaVersion, obl.SchemaVersion);
    }

    [Fact]
    public void ApplyDucking_write_failure_sets_obligation_to_restore_pending()
    {
        var writer = new VolumeWriterMock();
        var store = new ApplicationVolumeStateStore();
        var classifier = new DuckingSessionClassifier();
        var obligations = new ObligationRepoMock();
        var service = new VolumeDuckingService(writer, classifier, store, obligations, new EndpointSelectorMock());

        var chromeId = SessionId(200, "Chrome.exe");
        writer.ResultFor = _ => VolumeWriteResult.Failed;

        service.ApplyDucking(new[] { Session(200, "Chrome.exe", 0.8f) },
            DefaultSettings, "VoiceDuck.exe");

        var obl = obligations.Saved[0];
        Assert.Equal(RestorationStatus.RestorePending, obl.Status);
    }

    [Fact]
    public void ApplyDucking_multi_session_app_creates_one_obligation()
    {
        var writer = new VolumeWriterMock();
        var store = new ApplicationVolumeStateStore();
        var classifier = new DuckingSessionClassifier();
        var obligations = new ObligationRepoMock();
        var service = new VolumeDuckingService(writer, classifier, store, obligations, new EndpointSelectorMock());

        var sessions = new[]
        {
            new AudioSessionInfo(IdentityWithInst(200, "Chrome.exe", "inst-200"), 1.0f, false, DefaultExePath),
            new AudioSessionInfo(IdentityWithInst(200, "Chrome.exe", "inst-201"), 0.995f, false, DefaultExePath),
        };
        service.ApplyDucking(sessions, DefaultSettings, "VoiceDuck.exe");

        Assert.Single(obligations.Saved);
    }

    [Fact]
    public void ApplyDucking_second_poll_does_not_overwrite_baseline_in_obligation()
    {
        var writer = new VolumeWriterMock();
        var store = new ApplicationVolumeStateStore();
        var classifier = new DuckingSessionClassifier();
        var obligations = new ObligationRepoMock();
        var service = new VolumeDuckingService(writer, classifier, store, obligations, new EndpointSelectorMock());

        service.ApplyDucking(new[] { Session(200, "Chrome.exe", 0.8f) },
            DefaultSettings, "VoiceDuck.exe");

        writer.Calls.Clear();

        service.ApplyDucking(new[] { Session(200, "Chrome.exe", 0.4f) },
            DefaultSettings, "VoiceDuck.exe");

        var obl = obligations.Saved[0];
        Assert.Equal(0.8f, obl.BaselineVolume, 3);
        Assert.Equal(RestorationStatus.Ducked, obl.Status);
    }

    [Fact]
    public void ApplyDucking_partial_write_failure_one_session_makes_obligation_restore_pending()
    {
        var writer = new VolumeWriterMock();
        var store = new ApplicationVolumeStateStore();
        var classifier = new DuckingSessionClassifier();
        var obligations = new ObligationRepoMock();
        var service = new VolumeDuckingService(writer, classifier, store, obligations, new EndpointSelectorMock());

        var sessions = new[]
        {
            new AudioSessionInfo(IdentityWithInst(200, "Chrome.exe", "inst-200"), 1.0f, false, DefaultExePath),
            new AudioSessionInfo(IdentityWithInst(200, "Chrome.exe", "inst-201"), 0.995f, false, DefaultExePath),
        };

        var inst200 = SessionId(200, "Chrome.exe", DefaultDevice);
        writer.ResultFor = id => id == inst200 ? VolumeWriteResult.Succeeded : VolumeWriteResult.Failed;

        service.ApplyDucking(sessions, DefaultSettings, "VoiceDuck.exe");

        var obl = obligations.Saved[0];
        Assert.Equal(RestorationStatus.RestorePending, obl.Status);
    }

    [Fact]
    public void ApplyDucking_does_not_alter_unrelated_existing_obligation()
    {
        var writer = new VolumeWriterMock();
        var store = new ApplicationVolumeStateStore();
        var classifier = new DuckingSessionClassifier();
        var obligations = new ObligationRepoMock();

        var spotifyPath = @"C:\Spotify\spotify.exe";
        var spotifyKey = new ApplicationAudioIdentity(DefaultDevice, spotifyPath);
        var spotifyObl = new RestorationObligation(
            spotifyKey, 0.6f, RestorationStatus.Ducked,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        obligations.Existing = new List<RestorationObligation> { spotifyObl };

        var service = new VolumeDuckingService(writer, classifier, store, obligations, new EndpointSelectorMock());

        service.ApplyDucking(new[] { Session(200, "Chrome.exe", 0.8f) },
            DefaultSettings, "VoiceDuck.exe");

        Assert.Single(obligations.Saved, o =>
            o.Identity.Equals(spotifyKey) && o.Status == RestorationStatus.Ducked);
        Assert.Single(obligations.Saved, o =>
            o.Identity.Equals(AppKey) && o.Status == RestorationStatus.Ducked);
    }

    // ── RestoreVolumes ──

    [Fact]
    public void RestoreVolumes_restores_to_baseline()
    {
        var writer = new VolumeWriterMock();
        var store = new ApplicationVolumeStateStore();
        var classifier = new DuckingSessionClassifier();
        var obligations = new ObligationRepoMock();
        var service = new VolumeDuckingService(writer, classifier, store, obligations, new EndpointSelectorMock());

        service.ApplyDucking(new[] { Session(200, "Chrome.exe", 0.8f) },
            DefaultSettings, "VoiceDuck.exe");
        writer.Calls.Clear();

        service.RestoreVolumes(new[] { Session(200, "Chrome.exe", 0.4f) });

        Assert.Empty(store.GetAll());
        Assert.Contains(writer.Calls, c =>
            c.identity.Equals(SessionId(200, "Chrome.exe")) &&
            Math.Abs(c.volume - 0.8f) < 0.001f);
    }

    [Fact]
    public void RestoreVolumes_empty_store_does_nothing()
    {
        var writer = new VolumeWriterMock();
        var store = new ApplicationVolumeStateStore();
        var classifier = new DuckingSessionClassifier();
        var obligations = new ObligationRepoMock();
        var service = new VolumeDuckingService(writer, classifier, store, obligations, new EndpointSelectorMock());

        service.RestoreVolumes(Array.Empty<AudioSessionInfo>());

        Assert.Empty(writer.Calls);
    }

    [Fact]
    public void RestoreVolumes_no_matching_sessions_keeps_state()
    {
        var writer = new VolumeWriterMock();
        var store = new ApplicationVolumeStateStore();
        var classifier = new DuckingSessionClassifier();
        var obligations = new ObligationRepoMock();
        var service = new VolumeDuckingService(writer, classifier, store, obligations, new EndpointSelectorMock());

        store.Add(new ApplicationVolumeState(AppKey, 0.8f, isDucked: true));

        // No sessions matching AppKey
        service.RestoreVolumes(Array.Empty<AudioSessionInfo>());

        Assert.Equal(1, store.Count); // state preserved for deferred restore
    }

    [Fact]
    public void RestoreVolumes_mixed_sessions()
    {
        var writer = new VolumeWriterMock();
        var store = new ApplicationVolumeStateStore();
        var classifier = new DuckingSessionClassifier();
        var obligations = new ObligationRepoMock();
        var service = new VolumeDuckingService(writer, classifier, store, obligations, new EndpointSelectorMock());

        var chromePath = @"C:\Chrome\chrome.exe";
        var spotifyPath = @"C:\Spotify\spotify.exe";
        var chromeKey = new ApplicationAudioIdentity(DefaultDevice, chromePath);
        var spotifyKey = new ApplicationAudioIdentity(DefaultDevice, spotifyPath);

        store.Add(new ApplicationVolumeState(chromeKey, 0.8f, isDucked: true));
        store.Add(new ApplicationVolumeState(spotifyKey, 0.6f, isDucked: true));

        writer.ResultFor = id =>
            id.ProcessId == 1 ? VolumeWriteResult.Succeeded :
            id.ProcessId == 2 ? VolumeWriteResult.Failed :
            VolumeWriteResult.Succeeded;

        var sessions = new[]
        {
            Session(1, "Chrome.exe", 0.4f, chromePath),
            Session(2, "Spotify.exe", 0.3f, spotifyPath),
        };

        service.RestoreVolumes(sessions);

        // Chrome succeeded → removed
        Assert.False(store.TryGet(chromeKey, out _));
        // Spotify failed → kept
        Assert.True(store.TryGet(spotifyKey, out var spotifyState));
        Assert.True(spotifyState!.IsDucked);
        Assert.Equal(0.6f, spotifyState.BaselineVolume);
    }

    // ── ApplyDeferredRestores ──

    [Fact]
    public void ApplyDeferredRestores_restores_when_session_appears()
    {
        var writer = new VolumeWriterMock();
        var store = new ApplicationVolumeStateStore();
        var classifier = new DuckingSessionClassifier();
        var obligations = new ObligationRepoMock();
        var service = new VolumeDuckingService(writer, classifier, store, obligations, new EndpointSelectorMock());

        store.Add(new ApplicationVolumeState(AppKey, 0.8f, isDucked: true));

        service.ApplyDeferredRestores(new[] { Session(200, "Chrome.exe", 0.4f) });

        Assert.Empty(store.GetAll());
        Assert.Contains(writer.Calls, c =>
            c.identity.Equals(SessionId(200, "Chrome.exe")) &&
            Math.Abs(c.volume - 0.8f) < 0.001f);
    }

    [Fact]
    public void ApplyDeferredRestores_no_match_keeps_state()
    {
        var writer = new VolumeWriterMock();
        var store = new ApplicationVolumeStateStore();
        var classifier = new DuckingSessionClassifier();
        var obligations = new ObligationRepoMock();
        var service = new VolumeDuckingService(writer, classifier, store, obligations, new EndpointSelectorMock());

        store.Add(new ApplicationVolumeState(AppKey, 0.8f, isDucked: true));

        service.ApplyDeferredRestores(Array.Empty<AudioSessionInfo>());

        Assert.Equal(1, store.Count);
        Assert.Empty(writer.Calls);
    }

    [Fact]
    public void ApplyDeferredRestores_write_failed_keeps_state()
    {
        var writer = new VolumeWriterMock();
        var store = new ApplicationVolumeStateStore();
        var classifier = new DuckingSessionClassifier();
        var obligations = new ObligationRepoMock();
        var service = new VolumeDuckingService(writer, classifier, store, obligations, new EndpointSelectorMock());

        store.Add(new ApplicationVolumeState(AppKey, 0.8f, isDucked: true));

        var chromeId = SessionId(200, "Chrome.exe");
        writer.ResultFor = id => id.Equals(chromeId) ? VolumeWriteResult.Failed : VolumeWriteResult.Succeeded;

        service.ApplyDeferredRestores(new[] { Session(200, "Chrome.exe", 0.4f) });

        Assert.Equal(1, store.Count); // kept for retry
    }

    // ── Edge cases ──

    [Fact]
    public void SetDucked_toggle_works()
    {
        var store = new ApplicationVolumeStateStore();
        var state = new ApplicationVolumeState(AppKey, 0.8f, isDucked: false);
        store.Add(state);

        state.SetDucked(true);
        Assert.True(state.IsDucked);

        state.SetDucked(false);
        Assert.False(state.IsDucked);
    }

    [Fact]
    public void Same_app_different_devices_independent_restore()
    {
        var writer = new VolumeWriterMock();
        var store = new ApplicationVolumeStateStore();
        var classifier = new DuckingSessionClassifier();
        var obligations = new ObligationRepoMock();
        var selector = new EndpointSelectorMock { EndpointId = "device-A" };
        var service = new VolumeDuckingService(writer, classifier, store, obligations, selector);

        var devBPath = @"C:\App\app.exe";
        var devAKey = new ApplicationAudioIdentity("device-A", devBPath);
        var devBKey = new ApplicationAudioIdentity("device-B", devBPath);

        store.Add(new ApplicationVolumeState(devAKey, 0.8f, isDucked: true));
        store.Add(new ApplicationVolumeState(devBKey, 0.5f, isDucked: true));

        // Only device-A sessions present
        var sessions = new[]
        {
            new AudioSessionInfo(new AudioSessionIdentity(200, "Chrome.exe", "device-A", "inst-200"), 0.4f, false, devBPath),
        };

        service.RestoreVolumes(sessions);

        // device-A restored → removed
        Assert.False(store.TryGet(devAKey, out _));
        // device-B no sessions → kept
        Assert.True(store.TryGet(devBKey, out var bState));
        Assert.Equal(0.5f, bState!.BaselineVolume);
    }

    [Fact]
    public void RestoreVolumes_partial_failure_retries_on_next_poll()
    {
        var writer = new VolumeWriterMock();
        var store = new ApplicationVolumeStateStore();
        var classifier = new DuckingSessionClassifier();
        var obligations = new ObligationRepoMock();
        var service = new VolumeDuckingService(writer, classifier, store, obligations, new EndpointSelectorMock());

        store.Add(new ApplicationVolumeState(AppKey, 0.8f, isDucked: true));

        var idA = SessionId(200, "Chrome.exe", DefaultDevice);

        // First call: write fails → state kept for retry
        writer.ResultFor = id => id.Equals(idA) ? VolumeWriteResult.Failed : VolumeWriteResult.Succeeded;
        service.RestoreVolumes(new[] { new AudioSessionInfo(idA, 0.4f, false, DefaultExePath) });

        Assert.True(store.TryGet(AppKey, out _));
        writer.Calls.Clear();

        // Second call: write succeeds → state removed
        writer.ResultFor = _ => VolumeWriteResult.Succeeded;
        service.RestoreVolumes(new[] { new AudioSessionInfo(idA, 0.8f, false, DefaultExePath) });

        Assert.False(store.TryGet(AppKey, out _));
    }

    [Fact]
    public void RestoreVolumes_two_sessions_one_restored_state_removed()
    {
        var writer = new VolumeWriterMock();
        var store = new ApplicationVolumeStateStore();
        var classifier = new DuckingSessionClassifier();
        var obligations = new ObligationRepoMock();
        var service = new VolumeDuckingService(writer, classifier, store, obligations, new EndpointSelectorMock());

        // Two sessions ducked
        store.Add(new ApplicationVolumeState(AppKey, 0.8f, isDucked: true));

        var idA = SessionId(200, "Chrome.exe", DefaultDevice);

        // Only one session exists at restore time, restore succeeds → state removed
        service.RestoreVolumes(new[] { new AudioSessionInfo(idA, 0.4f, false, DefaultExePath) });

        Assert.False(store.TryGet(AppKey, out _));
    }

    [Fact]
    public void ApplyDeferredRestores_resumes_after_restore_with_no_sessions()
    {
        var writer = new VolumeWriterMock();
        var store = new ApplicationVolumeStateStore();
        var classifier = new DuckingSessionClassifier();
        var obligations = new ObligationRepoMock();
        var service = new VolumeDuckingService(writer, classifier, store, obligations, new EndpointSelectorMock());

        store.Add(new ApplicationVolumeState(AppKey, 0.8f, isDucked: true));

        // RestoreVolumes: no matching sessions → state kept (deferred)
        service.RestoreVolumes(Array.Empty<AudioSessionInfo>());
        Assert.True(store.TryGet(AppKey, out _));

        // ApplyDeferredRestores: matching session appears → baseline restored
        service.ApplyDeferredRestores(new[] { Session(200, "Chrome.exe", 0.4f) });

        Assert.False(store.TryGet(AppKey, out _));
        Assert.Contains(writer.Calls, c => Math.Abs(c.volume - 0.8f) < 0.001f);
    }

    [Fact]
    public void ApplyDucking_same_app_multi_device()
    {
        var writer = new VolumeWriterMock();
        var store = new ApplicationVolumeStateStore();
        var classifier = new DuckingSessionClassifier();
        var obligations = new ObligationRepoMock();
        var selector = new EndpointSelectorMock();
        var service = new VolumeDuckingService(writer, classifier, store, obligations, selector);

        selector.EndpointId = "device-A";
        var sessionsA = new[]
        {
            new AudioSessionInfo(new AudioSessionIdentity(200, "Chrome.exe", "device-A", "inst-200"), 1.0f, false, DefaultExePath),
        };
        service.ApplyDucking(sessionsA, DefaultSettings, "VoiceDuck.exe");

        selector.EndpointId = "device-B";
        var sessionsB = new[]
        {
            new AudioSessionInfo(new AudioSessionIdentity(200, "Chrome.exe", "device-B", "inst-201"), 0.5f, false, DefaultExePath),
        };
        service.ApplyDucking(sessionsB, DefaultSettings, "VoiceDuck.exe");

        Assert.Equal(2, store.Count);
        Assert.True(store.TryGet(new ApplicationAudioIdentity("device-A", DefaultExePath), out var a));
        Assert.Equal(1.0f, a!.BaselineVolume);
        Assert.True(store.TryGet(new ApplicationAudioIdentity("device-B", DefaultExePath), out var b));
        Assert.Equal(0.5f, b!.BaselineVolume);
    }

    // ── Restore obligation sync ──

    [Fact]
    public void RestoreVolumes_deletes_obligation_on_success()
    {
        var writer = new VolumeWriterMock();
        var store = new ApplicationVolumeStateStore();
        var classifier = new DuckingSessionClassifier();
        var obligations = new ObligationRepoMock();
        var service = new VolumeDuckingService(writer, classifier, store, obligations, new EndpointSelectorMock());

        obligations.Existing = new List<RestorationObligation>
        {
            new(AppKey, 0.8f, RestorationStatus.Ducked,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
        };

        store.Add(new ApplicationVolumeState(AppKey, 0.8f, isDucked: true));

        service.RestoreVolumes(new[] { Session(200, "Chrome.exe", 0.4f) });

        Assert.Empty(store.GetAll());
        Assert.Empty(obligations.Saved);
    }

    [Fact]
    public void RestoreVolumes_keeps_obligation_when_no_matching_sessions()
    {
        var writer = new VolumeWriterMock();
        var store = new ApplicationVolumeStateStore();
        var classifier = new DuckingSessionClassifier();
        var obligations = new ObligationRepoMock();
        var service = new VolumeDuckingService(writer, classifier, store, obligations, new EndpointSelectorMock());

        var existingObl = new RestorationObligation(
            AppKey, 0.8f, RestorationStatus.Ducked,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        obligations.Existing = new List<RestorationObligation> { existingObl };

        store.Add(new ApplicationVolumeState(AppKey, 0.8f, isDucked: true));

        service.RestoreVolumes(Array.Empty<AudioSessionInfo>());

        Assert.Equal(1, store.Count);
        Assert.Single(obligations.Saved, o =>
            o.Identity.Equals(AppKey) && o.Status == RestorationStatus.RestorePending);
    }

    [Fact]
    public void RestoreVolumes_keeps_obligation_on_write_failed()
    {
        var writer = new VolumeWriterMock();
        var store = new ApplicationVolumeStateStore();
        var classifier = new DuckingSessionClassifier();
        var obligations = new ObligationRepoMock();
        var service = new VolumeDuckingService(writer, classifier, store, obligations, new EndpointSelectorMock());

        var existingObl = new RestorationObligation(
            AppKey, 0.8f, RestorationStatus.Ducked,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        obligations.Existing = new List<RestorationObligation> { existingObl };

        store.Add(new ApplicationVolumeState(AppKey, 0.8f, isDucked: true));

        var chromeId = SessionId(200, "Chrome.exe");
        writer.ResultFor = _ => VolumeWriteResult.Failed;

        service.RestoreVolumes(new[] { Session(200, "Chrome.exe", 0.4f) });

        Assert.Equal(1, store.Count);
        Assert.Single(obligations.Saved, o =>
            o.Identity.Equals(AppKey) && o.Status == RestorationStatus.RestorePending);
    }

    [Fact]
    public void RestoreVolumes_mixed_results_keeps_failed_obligations()
    {
        var writer = new VolumeWriterMock();
        var store = new ApplicationVolumeStateStore();
        var classifier = new DuckingSessionClassifier();
        var obligations = new ObligationRepoMock();

        var chromePath = @"C:\Chrome\chrome.exe";
        var spotifyPath = @"C:\Spotify\spotify.exe";
        var chromeKey = new ApplicationAudioIdentity(DefaultDevice, chromePath);
        var spotifyKey = new ApplicationAudioIdentity(DefaultDevice, spotifyPath);

        obligations.Existing = new List<RestorationObligation>
        {
            new(chromeKey, 0.8f, RestorationStatus.Ducked,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
            new(spotifyKey, 0.6f, RestorationStatus.Ducked,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
        };

        store.Add(new ApplicationVolumeState(chromeKey, 0.8f, isDucked: true));
        store.Add(new ApplicationVolumeState(spotifyKey, 0.6f, isDucked: true));

        var service = new VolumeDuckingService(writer, classifier, store, obligations, new EndpointSelectorMock());

        writer.ResultFor = id =>
            id.ProcessId == 1 ? VolumeWriteResult.Succeeded :
            VolumeWriteResult.Failed;

        var sessions = new[]
        {
            Session(1, "Chrome.exe", 0.4f, chromePath),
            Session(2, "Spotify.exe", 0.3f, spotifyPath),
        };

        service.RestoreVolumes(sessions);

        Assert.False(store.TryGet(chromeKey, out _));
        Assert.True(store.TryGet(spotifyKey, out _));
        Assert.DoesNotContain(obligations.Saved, o => o.Identity.Equals(chromeKey));
        Assert.Single(obligations.Saved.Where(o =>
            o.Identity.Equals(spotifyKey) && o.Status == RestorationStatus.RestorePending));
    }

    [Fact]
    public void ApplyDeferredRestores_deletes_obligation_on_success()
    {
        var writer = new VolumeWriterMock();
        var store = new ApplicationVolumeStateStore();
        var classifier = new DuckingSessionClassifier();
        var obligations = new ObligationRepoMock();
        var service = new VolumeDuckingService(writer, classifier, store, obligations, new EndpointSelectorMock());

        obligations.Existing = new List<RestorationObligation>
        {
            new(AppKey, 0.8f, RestorationStatus.Ducked,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
        };

        store.Add(new ApplicationVolumeState(AppKey, 0.8f, isDucked: true));

        service.ApplyDeferredRestores(new[] { Session(200, "Chrome.exe", 0.4f) });

        Assert.Empty(store.GetAll());
        Assert.Empty(obligations.Saved);
    }

    [Fact]
    public void ApplyDeferredRestores_keeps_obligation_on_write_failed()
    {
        var writer = new VolumeWriterMock();
        var store = new ApplicationVolumeStateStore();
        var classifier = new DuckingSessionClassifier();
        var obligations = new ObligationRepoMock();
        var service = new VolumeDuckingService(writer, classifier, store, obligations, new EndpointSelectorMock());

        var existingObl = new RestorationObligation(
            AppKey, 0.8f, RestorationStatus.Ducked,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        obligations.Existing = new List<RestorationObligation> { existingObl };

        store.Add(new ApplicationVolumeState(AppKey, 0.8f, isDucked: true));

        writer.ResultFor = _ => VolumeWriteResult.Failed;

        service.ApplyDeferredRestores(new[] { Session(200, "Chrome.exe", 0.4f) });

        Assert.Equal(1, store.Count);
        Assert.Single(obligations.Saved, o =>
            o.Identity.Equals(AppKey) && o.Status == RestorationStatus.RestorePending);
    }

    [Fact]
    public void RestoreVolumes_retries_cleanup_after_save_all_failure()
    {
        var writer = new VolumeWriterMock();
        var store = new ApplicationVolumeStateStore();
        var classifier = new DuckingSessionClassifier();
        var obligations = new ObligationRepoMock();
        var service = new VolumeDuckingService(writer, classifier, store, obligations, new EndpointSelectorMock());

        var existingObl = new RestorationObligation(
            AppKey, 0.8f, RestorationStatus.Ducked,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        obligations.Existing = new List<RestorationObligation> { existingObl };

        store.Add(new ApplicationVolumeState(AppKey, 0.8f, isDucked: true));

        obligations.ShouldThrowOnSave = true;

        service.RestoreVolumes(new[] { Session(200, "Chrome.exe", 0.4f) });

        Assert.True(store.TryGet(AppKey, out var state));
        Assert.False(state!.IsDucked);

        writer.Calls.Clear();
        obligations.ShouldThrowOnSave = false;

        service.RestoreVolumes(new[] { Session(200, "Chrome.exe", 0.8f) });

        Assert.Empty(writer.Calls);
        Assert.False(store.TryGet(AppKey, out _));
        Assert.DoesNotContain(obligations.Saved, o => o.Identity.Equals(AppKey));
    }

    [Fact]
    public void ApplyDeferredRestores_retries_cleanup_after_save_all_failure()
    {
        var writer = new VolumeWriterMock();
        var store = new ApplicationVolumeStateStore();
        var classifier = new DuckingSessionClassifier();
        var obligations = new ObligationRepoMock();
        var service = new VolumeDuckingService(writer, classifier, store, obligations, new EndpointSelectorMock());

        var existingObl = new RestorationObligation(
            AppKey, 0.8f, RestorationStatus.Ducked,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        obligations.Existing = new List<RestorationObligation> { existingObl };

        store.Add(new ApplicationVolumeState(AppKey, 0.8f, isDucked: true));

        obligations.ShouldThrowOnSave = true;

        service.ApplyDeferredRestores(new[] { Session(200, "Chrome.exe", 0.4f) });

        Assert.True(store.TryGet(AppKey, out var state));
        Assert.False(state!.IsDucked);

        writer.Calls.Clear();
        obligations.ShouldThrowOnSave = false;

        service.ApplyDeferredRestores(new[] { Session(200, "Chrome.exe", 0.8f) });

        Assert.Empty(writer.Calls);
        Assert.False(store.TryGet(AppKey, out _));
        Assert.DoesNotContain(obligations.Saved, o => o.Identity.Equals(AppKey));
    }

    // ── Startup recovery ──

    [Fact]
    public void LoadAndPopulateStartupState_loads_obligations_to_state_store()
    {
        var writer = new VolumeWriterMock();
        var store = new ApplicationVolumeStateStore();
        var classifier = new DuckingSessionClassifier();
        var obligations = new ObligationRepoMock();
        var service = new VolumeDuckingService(writer, classifier, store, obligations, new EndpointSelectorMock());

        obligations.Existing = new List<RestorationObligation>
        {
            new(AppKey, 0.8f, RestorationStatus.Ducked,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
        };

        service.LoadAndPopulateStartupState();

        Assert.True(store.TryGet(AppKey, out var state));
        Assert.Equal(0.8f, state!.BaselineVolume);
        Assert.True(state.IsDucked);
    }

    [Fact]
    public void LoadAndPopulateStartupState_marks_all_as_restore_pending()
    {
        var writer = new VolumeWriterMock();
        var store = new ApplicationVolumeStateStore();
        var classifier = new DuckingSessionClassifier();
        var obligations = new ObligationRepoMock();
        var service = new VolumeDuckingService(writer, classifier, store, obligations, new EndpointSelectorMock());

        obligations.Existing = new List<RestorationObligation>
        {
            new(AppKey, 0.8f, RestorationStatus.Ducked,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
        };

        service.LoadAndPopulateStartupState();

        Assert.All(obligations.Saved, o =>
            Assert.Equal(RestorationStatus.RestorePending, o.Status));
    }

    [Fact]
    public void LoadAndPopulateStartupState_skips_existing_states()
    {
        var writer = new VolumeWriterMock();
        var store = new ApplicationVolumeStateStore();
        var classifier = new DuckingSessionClassifier();
        var obligations = new ObligationRepoMock();
        var service = new VolumeDuckingService(writer, classifier, store, obligations, new EndpointSelectorMock());

        store.Add(new ApplicationVolumeState(AppKey, 0.5f, isDucked: true));

        obligations.Existing = new List<RestorationObligation>
        {
            new(AppKey, 0.8f, RestorationStatus.Ducked,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
        };

        service.LoadAndPopulateStartupState();

        Assert.True(store.TryGet(AppKey, out var state));
        Assert.Equal(0.5f, state!.BaselineVolume);
    }

    [Fact]
    public void LoadAndPopulateStartupState_loads_valid_obligations_even_when_corrupt()
    {
        var writer = new VolumeWriterMock();
        var store = new ApplicationVolumeStateStore();
        var classifier = new DuckingSessionClassifier();
        var obligations = new ObligationRepoMock();
        obligations.WasCorrupt = true;
        var service = new VolumeDuckingService(writer, classifier, store, obligations, new EndpointSelectorMock());

        obligations.Existing = new List<RestorationObligation>
        {
            new(AppKey, 0.8f, RestorationStatus.Ducked,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
        };

        var result = service.LoadAndPopulateStartupState();

        Assert.True(store.TryGet(AppKey, out var state));
        Assert.Equal(0.8f, state!.BaselineVolume);
        Assert.True(result.WasCorrupt);
        Assert.True(result.Saved);
        Assert.Equal(1, result.LoadedCount);
    }

    [Fact]
    public void LoadAndPopulateStartupState_continues_when_save_fails()
    {
        var writer = new VolumeWriterMock();
        var store = new ApplicationVolumeStateStore();
        var classifier = new DuckingSessionClassifier();
        var obligations = new ObligationRepoMock();
        obligations.ShouldThrowOnSave = true;
        var service = new VolumeDuckingService(writer, classifier, store, obligations, new EndpointSelectorMock());

        obligations.Existing = new List<RestorationObligation>
        {
            new(AppKey, 0.8f, RestorationStatus.Ducked,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
        };

        var result = service.LoadAndPopulateStartupState();

        Assert.False(result.Saved);
        Assert.True(store.TryGet(AppKey, out var state));
        Assert.Equal(0.8f, state!.BaselineVolume);
    }

    [Fact]
    public void LoadAndPopulateStartupState_overwrites_totally_corrupt_file()
    {
        var writer = new VolumeWriterMock();
        var store = new ApplicationVolumeStateStore();
        var classifier = new DuckingSessionClassifier();
        var obligations = new ObligationRepoMock();
        obligations.WasCorrupt = true;
        var service = new VolumeDuckingService(writer, classifier, store, obligations, new EndpointSelectorMock());

        var result = service.LoadAndPopulateStartupState();

        Assert.True(result.WasCorrupt);
        Assert.True(result.Saved);
        Assert.Equal(0, result.LoadedCount);
        Assert.Empty(store.GetAll());
    }

    [Fact]
    public void LoadAndPopulateStartupState_skips_unresolved_identity()
    {
        var writer = new VolumeWriterMock();
        var store = new ApplicationVolumeStateStore();
        var classifier = new DuckingSessionClassifier();
        var obligations = new ObligationRepoMock();
        var service = new VolumeDuckingService(writer, classifier, store, obligations, new EndpointSelectorMock());

        obligations.Existing = new List<RestorationObligation>
        {
            new(new ApplicationAudioIdentity("", ""), 0.8f, RestorationStatus.Ducked,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
        };

        service.LoadAndPopulateStartupState();

        Assert.Empty(store.GetAll());
    }

    // ── Per-identity logging ──

    [Fact]
    public void RestoreVolumes_logs_SessionNotFound()
    {
        var writer = new VolumeWriterMock();
        var store = new ApplicationVolumeStateStore();
        var classifier = new DuckingSessionClassifier();
        var obligations = new ObligationRepoMock();
        var log = new LoggerMock();
        var service = new VolumeDuckingService(writer, classifier, store, obligations, new EndpointSelectorMock(), log);

        store.Add(new ApplicationVolumeState(AppKey, 0.8f, isDucked: true));

        service.RestoreVolumes(Array.Empty<AudioSessionInfo>());

        Assert.Contains(log.Messages, m => m.Contains("SessionNotFound"));
    }

    [Fact]
    public void RestoreVolumes_logs_Succeeded()
    {
        var writer = new VolumeWriterMock();
        var store = new ApplicationVolumeStateStore();
        var classifier = new DuckingSessionClassifier();
        var obligations = new ObligationRepoMock();
        var log = new LoggerMock();
        var service = new VolumeDuckingService(writer, classifier, store, obligations, new EndpointSelectorMock(), log);

        store.Add(new ApplicationVolumeState(AppKey, 0.8f, isDucked: true));
        var sessions = new[] { Session(1, "app.exe") };

        service.RestoreVolumes(sessions);

        Assert.Contains(log.Messages, m => m.Contains("result=Succeeded"));
    }

    [Fact]
    public void RestoreVolumes_logs_Failed()
    {
        var writer = new VolumeWriterMock();
        writer.ResultFor = _ => VolumeWriteResult.Failed;
        var store = new ApplicationVolumeStateStore();
        var classifier = new DuckingSessionClassifier();
        var obligations = new ObligationRepoMock();
        var log = new LoggerMock();
        var service = new VolumeDuckingService(writer, classifier, store, obligations, new EndpointSelectorMock(), log);

        store.Add(new ApplicationVolumeState(AppKey, 0.8f, isDucked: true));
        var sessions = new[] { Session(1, "app.exe") };

        service.RestoreVolumes(sessions);

        Assert.Contains(log.Messages, m => m.Contains("result=Failed"));
    }

    [Fact]
    public void LoadAndPopulateStartupState_logs_loaded()
    {
        var writer = new VolumeWriterMock();
        var store = new ApplicationVolumeStateStore();
        var classifier = new DuckingSessionClassifier();
        var obligations = new ObligationRepoMock();
        var log = new LoggerMock();
        var service = new VolumeDuckingService(writer, classifier, store, obligations, new EndpointSelectorMock(), log);

        obligations.Existing = new List<RestorationObligation>
        {
            new(AppKey, 0.8f, RestorationStatus.Ducked,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
        };

        service.LoadAndPopulateStartupState();

        Assert.Contains(log.Messages, m => m.Contains("StartupLoad"));
    }

    // ── No-op deferred restore ──

    [Fact]
    public void ApplyDeferredRestores_noop_does_not_save_again()
    {
        var writer = new VolumeWriterMock();
        var store = new ApplicationVolumeStateStore();
        var classifier = new DuckingSessionClassifier();
        var obligations = new ObligationRepoMock();
        var service = new VolumeDuckingService(writer, classifier, store, obligations, new EndpointSelectorMock());

        obligations.Existing = new List<RestorationObligation>
        {
            new(AppKey, 0.8f, RestorationStatus.Ducked,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
        };
        store.Add(new ApplicationVolumeState(AppKey, 0.8f, isDucked: true));

        // Call 1: Ducked → RestorePending transition → SaveAll
        service.ApplyDeferredRestores(Array.Empty<AudioSessionInfo>());
        Assert.Equal(1, obligations.SaveCount);

        // Calls 2,3: already RestorePending, no change → should NOT SaveAll
        service.ApplyDeferredRestores(Array.Empty<AudioSessionInfo>());
        service.ApplyDeferredRestores(Array.Empty<AudioSessionInfo>());

        Assert.Equal(1, obligations.SaveCount);
    }

    [Fact]
    public void ApplyDeferredRestores_noop_logs_already_pending_at_most_once()
    {
        var writer = new VolumeWriterMock();
        var store = new ApplicationVolumeStateStore();
        var classifier = new DuckingSessionClassifier();
        var obligations = new ObligationRepoMock();
        var log = new LoggerMock();
        var service = new VolumeDuckingService(writer, classifier, store, obligations, new EndpointSelectorMock(), log);

        obligations.Existing = new List<RestorationObligation>
        {
            new(AppKey, 0.8f, RestorationStatus.Ducked,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
        };
        store.Add(new ApplicationVolumeState(AppKey, 0.8f, isDucked: true));

        // Call 1: Ducked → RestorePending (logs "RestorePending", not "already_pending")
        service.ApplyDeferredRestores(Array.Empty<AudioSessionInfo>());

        // Calls 2,3: already RestorePending, "already_pending" logged at most once
        service.ApplyDeferredRestores(Array.Empty<AudioSessionInfo>());
        service.ApplyDeferredRestores(Array.Empty<AudioSessionInfo>());

        var alreadyPendingCount = log.Messages.Count(m => m.Contains("already_pending"));
        Assert.InRange(alreadyPendingCount, 0, 1);
    }

    [Fact]
    public void ApplyDeferredRestores_restores_after_noop()
    {
        var writer = new VolumeWriterMock();
        var store = new ApplicationVolumeStateStore();
        var classifier = new DuckingSessionClassifier();
        var obligations = new ObligationRepoMock();
        var service = new VolumeDuckingService(writer, classifier, store, obligations, new EndpointSelectorMock());

        obligations.Existing = new List<RestorationObligation>
        {
            new(AppKey, 0.8f, RestorationStatus.Ducked,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
        };
        store.Add(new ApplicationVolumeState(AppKey, 0.8f, isDucked: true));

        // No-op polls
        for (var i = 0; i < 3; i++)
            service.ApplyDeferredRestores(Array.Empty<AudioSessionInfo>());

        // Session re-appears
        service.ApplyDeferredRestores(new[] { Session(1, "app.exe") });

        Assert.Empty(store.GetAll());
    }

    // ── Lifecycle regression tests ──

    [Fact]
    public void Full_lifecycle_duck_restore_second_duck_uses_new_baseline()
    {
        var writer = new VolumeWriterMock();
        var store = new ApplicationVolumeStateStore();
        var classifier = new DuckingSessionClassifier();
        var obligations = new ObligationRepoMock();
        var service = new VolumeDuckingService(writer, classifier, store, obligations, new EndpointSelectorMock());

        // Phase 1: ApplyDucking at 0.8 → duck to 0.4
        service.ApplyDucking(new[] { Session(200, "Chrome.exe", 0.8f) },
            DefaultSettings, "VoiceDuck.exe");

        Assert.True(store.TryGet(AppKey, out var phase1));
        Assert.Equal(0.8f, phase1!.BaselineVolume);
        Assert.True(phase1.IsDucked);
        Assert.Contains(writer.Calls, c => Math.Abs(c.volume - 0.4f) < 0.001f);
        Assert.Single(obligations.Saved, o =>
            o.Identity.Equals(AppKey) && o.Status == RestorationStatus.Ducked);
        var firstSaveCount = obligations.SaveCount;

        // Phase 2: RestoreVolumes → back to 0.8
        writer.Calls.Clear();
        service.RestoreVolumes(new[] { Session(200, "Chrome.exe", 0.4f) });

        Assert.False(store.TryGet(AppKey, out _));
        Assert.Contains(writer.Calls, c => Math.Abs(c.volume - 0.8f) < 0.001f);
        Assert.Empty(obligations.Saved);

        // Phase 3: user changes volume to 0.6, second cycle
        writer.Calls.Clear();
        service.ApplyDucking(new[] { Session(200, "Chrome.exe", 0.6f) },
            DefaultSettings, "VoiceDuck.exe");

        Assert.True(store.TryGet(AppKey, out var phase3));
        Assert.Equal(0.6f, phase3!.BaselineVolume);
        Assert.True(phase3.IsDucked);
        Assert.Contains(writer.Calls, c => Math.Abs(c.volume - 0.3f) < 0.001f);
        Assert.DoesNotContain(writer.Calls, c => Math.Abs(c.volume - 0.2f) < 0.001f);
        Assert.Single(obligations.Saved, o =>
            o.Identity.Equals(AppKey) && o.Status == RestorationStatus.Ducked);

        // Phase 4: second restore → back to 0.6
        writer.Calls.Clear();
        service.RestoreVolumes(new[] { Session(200, "Chrome.exe", 0.3f) });

        Assert.False(store.TryGet(AppKey, out _));
        Assert.Empty(store.GetAll());
        Assert.Contains(writer.Calls, c => Math.Abs(c.volume - 0.6f) < 0.001f);
        Assert.Empty(obligations.Saved);
    }

    [Fact]
    public void Startup_recovery_while_vc_active_preserves_baseline_and_prevents_double_duck()
    {
        var writer = new VolumeWriterMock();
        var classifier = new DuckingSessionClassifier();
        var obligations = new ObligationRepoMock();

        // Service A with store A
        var storeA = new ApplicationVolumeStateStore();
        var serviceA = new VolumeDuckingService(writer, classifier, storeA, obligations, new EndpointSelectorMock());

        // Phase 1: ApplyDucking at 0.8 → duck to 0.4
        serviceA.ApplyDucking(new[] { Session(200, "Chrome.exe", 0.8f) },
            DefaultSettings, "VoiceDuck.exe");

        Assert.True(storeA.TryGet(AppKey, out var ducked));
        Assert.Equal(0.8f, ducked!.BaselineVolume);
        Assert.Single(obligations.Saved, o =>
            o.Identity.Equals(AppKey) && o.Status == RestorationStatus.Ducked);

        // Discard service A and store A. Service B gets a fresh store but same repository.
        var storeB = new ApplicationVolumeStateStore();
        var serviceB = new VolumeDuckingService(writer, classifier, storeB, obligations, new EndpointSelectorMock());

        // Phase 2: LoadAndPopulateStartupState loads baseline 0.8
        serviceB.LoadAndPopulateStartupState();

        Assert.True(storeB.TryGet(AppKey, out var loaded));
        Assert.Equal(0.8f, loaded!.BaselineVolume);
        Assert.True(loaded.IsDucked);

        // Phase 3: VC still active, current volume 0.4 → must keep baseline 0.8
        writer.Calls.Clear();
        serviceB.ApplyDucking(new[] { Session(200, "Chrome.exe", 0.4f) },
            DefaultSettings, "VoiceDuck.exe");

        Assert.True(storeB.TryGet(AppKey, out var reDucked));
        Assert.Equal(0.8f, reDucked!.BaselineVolume);
        Assert.Contains(writer.Calls, c => Math.Abs(c.volume - 0.4f) < 0.001f);
        Assert.DoesNotContain(writer.Calls, c => Math.Abs(c.volume - 0.2f) < 0.001f);
        Assert.Single(obligations.Saved, o =>
            o.Identity.Equals(AppKey) && o.Status == RestorationStatus.Ducked &&
            Math.Abs(o.BaselineVolume - 0.8f) < 0.001f);

        // Phase 4: VC disconnect → restore to 0.8
        writer.Calls.Clear();
        serviceB.RestoreVolumes(new[] { Session(200, "Chrome.exe", 0.4f) });

        Assert.False(storeB.TryGet(AppKey, out _));
        Assert.Empty(storeB.GetAll());
        Assert.Contains(writer.Calls, c => Math.Abs(c.volume - 0.8f) < 0.001f);
        Assert.Empty(obligations.Saved);
    }

    [Fact]
    public void Deferred_restore_lifecycle_recovers_after_session_reappears()
    {
        var writer = new VolumeWriterMock();
        var store = new ApplicationVolumeStateStore();
        var classifier = new DuckingSessionClassifier();
        var obligations = new ObligationRepoMock();
        var service = new VolumeDuckingService(writer, classifier, store, obligations, new EndpointSelectorMock());

        // Phase 1: ApplyDucking → duck to 0.4, obligation Ducked
        service.ApplyDucking(new[] { Session(200, "Chrome.exe", 0.8f) },
            DefaultSettings, "VoiceDuck.exe");

        Assert.True(store.TryGet(AppKey, out var ducked));
        Assert.Equal(0.8f, ducked!.BaselineVolume);
        Assert.Single(obligations.Saved, o =>
            o.Identity.Equals(AppKey) && o.Status == RestorationStatus.Ducked);

        // Phase 2: session disappears → RestoreVolumes keeps state for deferred
        writer.Calls.Clear();
        service.RestoreVolumes(Array.Empty<AudioSessionInfo>());

        Assert.True(store.TryGet(AppKey, out var deferred));
        Assert.True(deferred!.IsDucked);
        var afterRestoreSaveCount = obligations.SaveCount;
        Assert.Single(obligations.Saved, o =>
            o.Identity.Equals(AppKey) && o.Status == RestorationStatus.RestorePending);

        // Phase 3: no-op polls must not increase SaveAll count
        for (var i = 0; i < 3; i++)
            service.ApplyDeferredRestores(Array.Empty<AudioSessionInfo>());

        Assert.Equal(afterRestoreSaveCount, obligations.SaveCount);
        Assert.Empty(writer.Calls);

        // Phase 4: session reappears → ApplyDeferredRestores restores to baseline
        service.ApplyDeferredRestores(new[] { Session(200, "Chrome.exe", 0.4f) });

        Assert.False(store.TryGet(AppKey, out _));
        Assert.Empty(store.GetAll());
        Assert.Contains(writer.Calls, c => Math.Abs(c.volume - 0.8f) < 0.001f);
        Assert.Empty(obligations.Saved);
    }

    [Fact]
    public void Multi_device_partial_restore_completes_each_device_independently()
    {
        var writer = new VolumeWriterMock();
        var store = new ApplicationVolumeStateStore();
        var classifier = new DuckingSessionClassifier();
        var obligations = new ObligationRepoMock();
        var selector = new EndpointSelectorMock();
        var service = new VolumeDuckingService(writer, classifier, store, obligations, selector);

        var devAKey = new ApplicationAudioIdentity("device-A", DefaultExePath);
        var devBKey = new ApplicationAudioIdentity("device-B", DefaultExePath);

        // Phase 1: select device-A, duck Chrome on device-A (1.0→0.5)
        selector.EndpointId = "device-A";
        var duckA = new[]
        {
            new AudioSessionInfo(
                new AudioSessionIdentity(200, "Chrome.exe", "device-A", "inst-200"),
                1.0f, false, DefaultExePath),
        };
        service.ApplyDucking(duckA, DefaultSettings, "VoiceDuck.exe");

        Assert.True(store.TryGet(devAKey, out var devA));
        Assert.Equal(1.0f, devA!.BaselineVolume);

        // Phase 2: switch to device-B, duck Chrome on device-B (0.8→0.4)
        selector.EndpointId = "device-B";
        var duckB = new[]
        {
            new AudioSessionInfo(
                new AudioSessionIdentity(300, "Chrome.exe", "device-B", "inst-300"),
                0.8f, false, DefaultExePath),
        };
        service.ApplyDucking(duckB, DefaultSettings, "VoiceDuck.exe");

        Assert.True(store.TryGet(devAKey, out devA));
        Assert.Equal(1.0f, devA!.BaselineVolume);
        Assert.True(store.TryGet(devBKey, out var devB));
        Assert.Equal(0.8f, devB!.BaselineVolume);
        Assert.Equal(2, obligations.Saved.Count);

        // Phase 3: select device-A, restore only device-A
        writer.Calls.Clear();
        selector.EndpointId = "device-A";
        var restoreA = new[]
        {
            new AudioSessionInfo(
                new AudioSessionIdentity(200, "Chrome.exe", "device-A", "inst-200"),
                0.5f, false, DefaultExePath),
        };
        service.RestoreVolumes(restoreA);

        Assert.False(store.TryGet(devAKey, out _));
        Assert.True(store.TryGet(devBKey, out var afterPartial));
        Assert.True(afterPartial!.IsDucked);
        Assert.Equal(0.8f, afterPartial.BaselineVolume);
        Assert.Contains(writer.Calls, c =>
            c.identity.RenderDeviceId == "device-A" && Math.Abs(c.volume - 1.0f) < 0.001f);
        Assert.DoesNotContain(writer.Calls, c => c.identity.RenderDeviceId == "device-B");
        Assert.Single(obligations.Saved, o => o.Identity.Equals(devBKey));

        // Phase 4: select device-B, restore device-B
        writer.Calls.Clear();
        selector.EndpointId = "device-B";
        var restoreB = new[]
        {
            new AudioSessionInfo(
                new AudioSessionIdentity(300, "Chrome.exe", "device-B", "inst-300"),
                0.4f, false, DefaultExePath),
        };
        service.RestoreVolumes(restoreB);

        Assert.False(store.TryGet(devBKey, out _));
        Assert.Empty(store.GetAll());
        Assert.Contains(writer.Calls, c =>
            c.identity.RenderDeviceId == "device-B" && Math.Abs(c.volume - 0.8f) < 0.001f);
        Assert.Empty(obligations.Saved);
    }
}
