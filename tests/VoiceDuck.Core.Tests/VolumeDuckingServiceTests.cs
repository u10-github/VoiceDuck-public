namespace VoiceDuck.Core.Tests;

public class VolumeDuckingServiceTests
{
    private const string DefaultDevice = "default-device";

    private static readonly VoiceDuckSettings DefaultSettings = new(
        new DuckingPolicy(0.3, 10),
        new[] { new TriggerApp("Discord.exe") },
        Array.Empty<ExcludeApp>());

    private static readonly VoiceDuckSettings Ratio100 = new(
        new DuckingPolicy(1.0, 10),
        new[] { new TriggerApp("Discord.exe") },
        Array.Empty<ExcludeApp>());

    private static AudioSessionIdentity SessionId(uint pid, string name) =>
        new(pid, name, DefaultDevice, $"inst-{pid}");

    private static AudioSessionInfo Session(uint pid, string name, float vol = 0.8f) =>
        new(SessionId(pid, name), vol, false);

    private sealed class VolumeWriterMock : IAudioSessionVolumeWriter
    {
        public List<(AudioSessionIdentity identity, float volume)> Calls { get; } = new();

        public void SetVolume(AudioSessionIdentity identity, float volume)
        {
            Calls.Add((identity, volume));
        }
    }

    [Fact]
    public void Duck_other_sessions_saves_snapshot_and_sets_ducked_volume()
    {
        var writer = new VolumeWriterMock();
        var store = new VolumeSnapshotStore();
        var classifier = new DuckingSessionClassifier();
        var service = new VolumeDuckingService(writer, classifier, store);

        var sessions = new[]
        {
            Session(100, "Discord.exe", 0.8f),
            Session(200, "Chrome.exe", 0.8f),
        };

        service.ApplyDucking(sessions, DefaultSettings, "VoiceDuck.exe");

        var chromeId = SessionId(200, "Chrome.exe");
        Assert.True(store.Contains(chromeId));
        Assert.True(store.TryGet(chromeId, out var snapshot));
        Assert.Equal(0.8f, snapshot!.OriginalVolume);
        Assert.Contains(writer.Calls, c => c.identity.Equals(chromeId) && Math.Abs(c.volume - 0.24f) < 0.001f);
    }

    [Fact]
    public void Protect_trigger_app_does_not_save_or_set_volume()
    {
        var writer = new VolumeWriterMock();
        var store = new VolumeSnapshotStore();
        var classifier = new DuckingSessionClassifier();
        var service = new VolumeDuckingService(writer, classifier, store);

        var sessions = new[]
        {
            Session(100, "Discord.exe", 0.8f),
        };

        service.ApplyDucking(sessions, DefaultSettings, "VoiceDuck.exe");

        var discordId = SessionId(100, "Discord.exe");
        Assert.False(store.Contains(discordId));
        Assert.DoesNotContain(writer.Calls, c => c.identity.Equals(discordId));
    }

    [Fact]
    public void Protect_exclude_app_does_not_save_or_set_volume()
    {
        var writer = new VolumeWriterMock();
        var store = new VolumeSnapshotStore();
        var classifier = new DuckingSessionClassifier();
        var settings = new VoiceDuckSettings(
            new DuckingPolicy(),
            new[] { new TriggerApp("Discord.exe") },
            new[] { new ExcludeApp("ScreenShare.exe") });
        var service = new VolumeDuckingService(writer, classifier, store);

        var sessions = new[]
        {
            Session(100, "ScreenShare.exe", 0.8f),
        };

        service.ApplyDucking(sessions, settings, "VoiceDuck.exe");

        var ssId = SessionId(100, "ScreenShare.exe");
        Assert.False(store.Contains(ssId));
        Assert.DoesNotContain(writer.Calls, c => c.identity.Equals(ssId));
    }

    [Fact]
    public void Protect_voice_duck_itself_does_not_save_or_set()
    {
        var writer = new VolumeWriterMock();
        var store = new VolumeSnapshotStore();
        var classifier = new DuckingSessionClassifier();
        var service = new VolumeDuckingService(writer, classifier, store);

        var sessions = new[]
        {
            Session(999, "VoiceDuck.exe", 0.8f),
        };

        service.ApplyDucking(sessions, DefaultSettings, "VoiceDuck.exe");

        var vdId = SessionId(999, "VoiceDuck.exe");
        Assert.False(store.Contains(vdId));
        Assert.DoesNotContain(writer.Calls, c => c.identity.Equals(vdId));
    }

    [Fact]
    public void Ducking_ratio_1_0_sets_same_volume()
    {
        var writer = new VolumeWriterMock();
        var store = new VolumeSnapshotStore();
        var classifier = new DuckingSessionClassifier();
        var service = new VolumeDuckingService(writer, classifier, store);

        var sessions = new[]
        {
            Session(200, "Chrome.exe", 0.8f),
        };

        service.ApplyDucking(sessions, Ratio100, "VoiceDuck.exe");

        var chromeId = SessionId(200, "Chrome.exe");
        Assert.Contains(writer.Calls, c => c.identity.Equals(chromeId) && Math.Abs(c.volume - 0.8f) < 0.001f);
    }

    [Fact]
    public void Session_already_snapshot_uses_original_volume_as_baseline()
    {
        var writer = new VolumeWriterMock();
        var store = new VolumeSnapshotStore();
        var classifier = new DuckingSessionClassifier();
        var service = new VolumeDuckingService(writer, classifier, store);

        var chromeId = SessionId(200, "Chrome.exe");
        store.Add(new VolumeSnapshot(chromeId, 0.8f));

        var sessions = new[]
        {
            Session(200, "Chrome.exe", 0.5f), // current volume differs from original
        };

        service.ApplyDucking(sessions, DefaultSettings, "VoiceDuck.exe");

        Assert.True(store.TryGet(chromeId, out var snapshot));
        Assert.Equal(0.8f, snapshot!.OriginalVolume);
        // Must use 0.8 * 0.3 = 0.24, not 0.5 * 0.3 = 0.15
        Assert.Contains(writer.Calls, c => c.identity.Equals(chromeId) && Math.Abs(c.volume - 0.24f) < 0.001f);
    }

    [Fact]
    public void Repeated_apply_ducking_does_not_compound_ratio()
    {
        var writer = new VolumeWriterMock();
        var store = new VolumeSnapshotStore();
        var classifier = new DuckingSessionClassifier();
        var service = new VolumeDuckingService(writer, classifier, store);

        var sessions = new[]
        {
            Session(200, "Chrome.exe", 0.8f),
        };

        // First pass: saves 0.8, sets 0.24
        service.ApplyDucking(sessions, DefaultSettings, "VoiceDuck.exe");

        // Simulate session volume now at ducked value
        var duckedSessions = new[]
        {
            Session(200, "Chrome.exe", 0.24f),
        };

        // Second pass: must NOT compound (0.24 * 0.3 = 0.072)
        // Must use original 0.8 and set 0.24 again
        service.ApplyDucking(duckedSessions, DefaultSettings, "VoiceDuck.exe");

        var chromeId = SessionId(200, "Chrome.exe");
        var lastCall = writer.Calls.Last(c => c.identity.Equals(chromeId));
        Assert.Equal(0.24f, lastCall.volume, 3);
        Assert.Single(store.GetAll());
    }

    [Fact]
    public void Empty_sessions_does_nothing()
    {
        var writer = new VolumeWriterMock();
        var store = new VolumeSnapshotStore();
        var classifier = new DuckingSessionClassifier();
        var service = new VolumeDuckingService(writer, classifier, store);

        service.ApplyDucking(Array.Empty<AudioSessionInfo>(), DefaultSettings, "VoiceDuck.exe");

        Assert.Empty(store.GetAll());
        Assert.Empty(writer.Calls);
    }

    [Fact]
    public void Multiple_non_trigger_sessions_all_ducked()
    {
        var writer = new VolumeWriterMock();
        var store = new VolumeSnapshotStore();
        var classifier = new DuckingSessionClassifier();
        var service = new VolumeDuckingService(writer, classifier, store);

        var sessions = new[]
        {
            Session(1, "Chrome.exe", 0.8f),
            Session(2, "Spotify.exe", 0.6f),
            Session(3, "Game.exe", 1.0f),
        };

        service.ApplyDucking(sessions, DefaultSettings, "VoiceDuck.exe");

        Assert.Equal(3, store.Count);
        Assert.Equal(3, writer.Calls.Count);
    }

    [Fact]
    public void Mixed_sessions_only_duck_non_protected()
    {
        var writer = new VolumeWriterMock();
        var store = new VolumeSnapshotStore();
        var classifier = new DuckingSessionClassifier();
        var service = new VolumeDuckingService(writer, classifier, store);

        var sessions = new[]
        {
            Session(1, "Discord.exe", 0.8f),
            Session(2, "Chrome.exe", 0.8f),
            Session(3, "VoiceDuck.exe", 0.8f),
        };

        service.ApplyDucking(sessions, DefaultSettings, "VoiceDuck.exe");

        Assert.Equal(1, store.Count);
        Assert.Single(writer.Calls);
        Assert.True(store.Contains(SessionId(2, "Chrome.exe")));
    }

    [Fact]
    public void Unresolved_session_instance_is_not_snapshotted_or_ducked()
    {
        var writer = new VolumeWriterMock();
        var store = new VolumeSnapshotStore();
        var classifier = new DuckingSessionClassifier();
        var service = new VolumeDuckingService(writer, classifier, store);

        var unresolvedId = new AudioSessionIdentity(100, "Chrome.exe", "device-A", "");
        var sessions = new[]
        {
            new AudioSessionInfo(unresolvedId, 0.8f, false),
        };

        service.ApplyDucking(sessions, DefaultSettings, "VoiceDuck.exe");

        Assert.Empty(store.GetAll());
        Assert.Empty(writer.Calls);
    }

    [Fact]
    public void Multiple_unresolved_sessions_on_same_device_are_not_collided_or_ducked()
    {
        var writer = new VolumeWriterMock();
        var store = new VolumeSnapshotStore();
        var classifier = new DuckingSessionClassifier();
        var service = new VolumeDuckingService(writer, classifier, store);

        var sessions = new[]
        {
            new AudioSessionInfo(new AudioSessionIdentity(100, "Chrome.exe", "device-A", ""), 1.0f, false),
            new AudioSessionInfo(new AudioSessionIdentity(200, "Game.exe", "device-A", ""), 0.5f, false),
        };

        service.ApplyDucking(sessions, DefaultSettings, "VoiceDuck.exe");

        // Neither session should be snapshotted (both unresolved)
        Assert.Empty(store.GetAll());
        Assert.Empty(writer.Calls);
    }

    [Fact]
    public void Resolved_session_alongside_unresolved_ones_is_ducked_correctly()
    {
        var writer = new VolumeWriterMock();
        var store = new VolumeSnapshotStore();
        var classifier = new DuckingSessionClassifier();
        var service = new VolumeDuckingService(writer, classifier, store);

        var sessions = new[]
        {
            new AudioSessionInfo(new AudioSessionIdentity(100, "Chrome.exe", "device-A", ""), 1.0f, false),
            Session(200, "Spotify.exe", 0.8f), // resolved: default-device / inst-200
        };

        service.ApplyDucking(sessions, DefaultSettings, "VoiceDuck.exe");

        // Only the resolved session should be snapshotted and ducked
        Assert.Equal(1, store.Count);
        Assert.Single(writer.Calls);

        var spotifyId = SessionId(200, "Spotify.exe");
        Assert.Contains(writer.Calls, c => c.identity.Equals(spotifyId));
    }

    [Fact]
    public void Pid_0_session_is_not_snapshotted_or_ducked()
    {
        var writer = new VolumeWriterMock();
        var store = new VolumeSnapshotStore();
        var classifier = new DuckingSessionClassifier();
        var service = new VolumeDuckingService(writer, classifier, store);

        var sessions = new[]
        {
            Session(0, "@%SystemRoot%\\System32\\AudioSrv.Dll,-202", 1.0f),
        };

        service.ApplyDucking(sessions, DefaultSettings, "VoiceDuck.exe");

        Assert.Empty(store.GetAll());
        Assert.Empty(writer.Calls);
    }

    [Fact]
    public void Restore_volumes_returns_all_snapshots_to_original()
    {
        var writer = new VolumeWriterMock();
        var store = new VolumeSnapshotStore();
        var classifier = new DuckingSessionClassifier();
        var service = new VolumeDuckingService(writer, classifier, store);

        var sessions = new[]
        {
            Session(1, "Chrome.exe", 0.8f),
            Session(2, "Spotify.exe", 0.6f),
        };

        service.ApplyDucking(sessions, DefaultSettings, "VoiceDuck.exe");
        writer.Calls.Clear();

        service.RestoreVolumes();

        var chromeId = SessionId(1, "Chrome.exe");
        var spotifyId = SessionId(2, "Spotify.exe");
        Assert.Contains(writer.Calls, c => c.identity.Equals(chromeId) && Math.Abs(c.volume - 0.8f) < 0.001f);
        Assert.Contains(writer.Calls, c => c.identity.Equals(spotifyId) && Math.Abs(c.volume - 0.6f) < 0.001f);
    }

    [Fact]
    public void Restore_volumes_clears_snapshot_store()
    {
        var writer = new VolumeWriterMock();
        var store = new VolumeSnapshotStore();
        var classifier = new DuckingSessionClassifier();
        var service = new VolumeDuckingService(writer, classifier, store);

        var sessions = new[]
        {
            Session(1, "Chrome.exe", 0.8f),
        };

        service.ApplyDucking(sessions, DefaultSettings, "VoiceDuck.exe");
        Assert.Equal(1, store.Count);

        service.RestoreVolumes();
        Assert.Empty(store.GetAll());
    }

    [Fact]
    public void Restore_volumes_with_empty_store_does_nothing()
    {
        var writer = new VolumeWriterMock();
        var store = new VolumeSnapshotStore();
        var classifier = new DuckingSessionClassifier();
        var service = new VolumeDuckingService(writer, classifier, store);

        service.RestoreVolumes();

        Assert.Empty(writer.Calls);
        Assert.Empty(store.GetAll());
    }

    [Fact]
    public void Restore_volumes_preserves_snapshot_original_volume_independent_of_current()
    {
        var writer = new VolumeWriterMock();
        var store = new VolumeSnapshotStore();
        var classifier = new DuckingSessionClassifier();
        var service = new VolumeDuckingService(writer, classifier, store);

        var sessions = new[]
        {
            Session(1, "Chrome.exe", 0.8f),
        };

        service.ApplyDucking(sessions, DefaultSettings, "VoiceDuck.exe");

        store.Add(new VolumeSnapshot(SessionId(1, "Chrome.exe"), 0.9f));

        writer.Calls.Clear();
        service.RestoreVolumes();

        var chromeId = SessionId(1, "Chrome.exe");
        Assert.Contains(writer.Calls, c => c.identity.Equals(chromeId) && Math.Abs(c.volume - 0.9f) < 0.001f);
    }

    [Fact]
    public void New_session_on_second_apply_ducking_is_snapshotted_and_ducked()
    {
        var writer = new VolumeWriterMock();
        var store = new VolumeSnapshotStore();
        var classifier = new DuckingSessionClassifier();
        var service = new VolumeDuckingService(writer, classifier, store);

        var firstBatch = new[]
        {
            Session(1, "Chrome.exe", 0.8f),
        };

        service.ApplyDucking(firstBatch, DefaultSettings, "VoiceDuck.exe");

        Assert.Single(store.GetAll());
        Assert.Single(writer.Calls);
        Assert.Contains(writer.Calls, c => c.identity.Equals(SessionId(1, "Chrome.exe")));

        writer.Calls.Clear();

        var secondBatch = new[]
        {
            Session(1, "Chrome.exe", 0.3f),
            Session(2, "Spotify.exe", 0.6f),
        };

        service.ApplyDucking(secondBatch, DefaultSettings, "VoiceDuck.exe");

        var spotifyId = SessionId(2, "Spotify.exe");
        Assert.Equal(2, store.Count);
        Assert.True(store.Contains(spotifyId));
        Assert.Contains(writer.Calls, c => c.identity.Equals(spotifyId));
        Assert.True(store.TryGet(spotifyId, out var spotifySnap));
        Assert.Equal(0.6f, spotifySnap!.OriginalVolume);
    }

    [Fact]
    public void Same_pid_different_device_sessions_are_ducked_independently()
    {
        var writer = new VolumeWriterMock();
        var store = new VolumeSnapshotStore();
        var classifier = new DuckingSessionClassifier();
        var service = new VolumeDuckingService(writer, classifier, store);

        var sessions = new[]
        {
            Session(100, "Game.exe", 1.0f),                         // device=default-device, inst=inst-100
            new AudioSessionInfo(new AudioSessionIdentity(100, "Game.exe", "device-B", "inst-200"), 0.3f, false),
        };

        service.ApplyDucking(sessions, DefaultSettings, "VoiceDuck.exe");

        Assert.Equal(2, store.Count);

        var defaultId = SessionId(100, "Game.exe");
        Assert.True(store.TryGet(defaultId, out var snapDefault));
        Assert.Equal(1.0f, snapDefault!.OriginalVolume);

        var otherId = new AudioSessionIdentity(100, "Game.exe", "device-B", "inst-200");
        Assert.True(store.TryGet(otherId, out var snapOther));
        Assert.Equal(0.3f, snapOther!.OriginalVolume);

        // Each session should receive a separate SetVolume call with the correct ducked volume
        Assert.Equal(2, writer.Calls.Count);
        Assert.Contains(writer.Calls, c => c.identity.Equals(defaultId) && Math.Abs(c.volume - 0.3f) < 0.001f);
        Assert.Contains(writer.Calls, c => c.identity.Equals(otherId) && Math.Abs(c.volume - 0.09f) < 0.001f);
    }
}
