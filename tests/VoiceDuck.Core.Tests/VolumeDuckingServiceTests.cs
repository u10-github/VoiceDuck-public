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

    // ── ApplyDucking ──

    [Fact]
    public void ApplyDucking_creates_state_and_sets_ducked_volume()
    {
        var writer = new VolumeWriterMock();
        var store = new ApplicationVolumeStateStore();
        var classifier = new DuckingSessionClassifier();
        var service = new VolumeDuckingService(writer, classifier, store);

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
        var service = new VolumeDuckingService(writer, classifier, store);

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
        var service = new VolumeDuckingService(writer, classifier, store);

        var sessions = new[]
        {
            Session(200, "Chrome.exe", 0.5f),
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
        var service = new VolumeDuckingService(writer, classifier, store);

        var sessions = new[]
        {
            Session(200, "Chrome.exe", 1.0f, DefaultExePath),
            new AudioSessionInfo(SessionId(200, "Chrome.exe", "device-B"), 0.3f, false, DefaultExePath),
        };
        service.ApplyDucking(sessions, DefaultSettings, "VoiceDuck.exe");

        Assert.Equal(2, store.Count);
        Assert.True(store.TryGet(new ApplicationAudioIdentity("device-B", DefaultExePath), out _));
    }

    [Fact]
    public void ApplyDucking_protects_trigger_apps()
    {
        var writer = new VolumeWriterMock();
        var store = new ApplicationVolumeStateStore();
        var classifier = new DuckingSessionClassifier();
        var service = new VolumeDuckingService(writer, classifier, store);

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
        var service = new VolumeDuckingService(writer, classifier, store);

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
        var service = new VolumeDuckingService(writer, classifier, store);

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
        var service = new VolumeDuckingService(writer, classifier, store);

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
        var service = new VolumeDuckingService(writer, classifier, store);

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
        var service = new VolumeDuckingService(writer, classifier, store);

        var sessions = new[]
        {
            new AudioSessionInfo(IdentityWithInst(200, "Chrome.exe", "inst-200"), 1.0f, false, DefaultExePath),
            new AudioSessionInfo(IdentityWithInst(200, "Chrome.exe", "inst-201"), 0.5f, false, DefaultExePath),
        };

        var inst200 = SessionId(200, "Chrome.exe", DefaultDevice);
        writer.ResultFor = id => id == inst200 ? VolumeWriteResult.Succeeded : VolumeWriteResult.Failed;

        service.ApplyDucking(sessions, DefaultSettings, "VoiceDuck.exe");

        Assert.True(store.TryGet(AppKey, out _));
    }

    // ── RestoreVolumes ──

    [Fact]
    public void RestoreVolumes_restores_to_baseline()
    {
        var writer = new VolumeWriterMock();
        var store = new ApplicationVolumeStateStore();
        var classifier = new DuckingSessionClassifier();
        var service = new VolumeDuckingService(writer, classifier, store);

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
        var service = new VolumeDuckingService(writer, classifier, store);

        service.RestoreVolumes(Array.Empty<AudioSessionInfo>());

        Assert.Empty(writer.Calls);
    }

    [Fact]
    public void RestoreVolumes_no_matching_sessions_keeps_state()
    {
        var writer = new VolumeWriterMock();
        var store = new ApplicationVolumeStateStore();
        var classifier = new DuckingSessionClassifier();
        var service = new VolumeDuckingService(writer, classifier, store);

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
        var service = new VolumeDuckingService(writer, classifier, store);

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
        var service = new VolumeDuckingService(writer, classifier, store);

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
        var service = new VolumeDuckingService(writer, classifier, store);

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
        var service = new VolumeDuckingService(writer, classifier, store);

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
        var service = new VolumeDuckingService(writer, classifier, store);

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
        var service = new VolumeDuckingService(writer, classifier, store);

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
        var service = new VolumeDuckingService(writer, classifier, store);

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
        var service = new VolumeDuckingService(writer, classifier, store);

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
        var service = new VolumeDuckingService(writer, classifier, store);

        var sessions = new[]
        {
            new AudioSessionInfo(new AudioSessionIdentity(200, "Chrome.exe", "device-A", "inst-200"), 1.0f, false, DefaultExePath),
            new AudioSessionInfo(new AudioSessionIdentity(200, "Chrome.exe", "device-B", "inst-201"), 0.5f, false, DefaultExePath),
        };

        service.ApplyDucking(sessions, DefaultSettings, "VoiceDuck.exe");

        Assert.Equal(2, store.Count);
        Assert.True(store.TryGet(new ApplicationAudioIdentity("device-A", DefaultExePath), out var a));
        Assert.Equal(1.0f, a!.BaselineVolume);
        Assert.True(store.TryGet(new ApplicationAudioIdentity("device-B", DefaultExePath), out var b));
        Assert.Equal(0.5f, b!.BaselineVolume);
    }
}
