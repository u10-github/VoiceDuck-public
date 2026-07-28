using VoiceDuck.App.Console;
using VoiceDuck.Core;
using VoiceDuck.Infrastructure;

namespace VoiceDuck.App.Console.Tests;

public class ConsoleLoggerEndpointSelectionTests
{
    private const string DefaultEndpoint = "{DefaultMultimedia}";
    private const string NonDefaultEndpoint = "{NonDefault}";
    private const string DefaultExePath = @"C:\Program Files\App\app.exe";

    private static readonly VoiceDuckSettings DefaultSettings = new(
        new DuckingPolicy(0.5, 10),
        new[] { new TriggerApp("Discord.exe") },
        Array.Empty<ExcludeApp>());

    private static AudioSessionIdentity SessionId(uint pid, string name, string device) =>
        new(pid, name, device, $"inst-{pid}");

    private static AudioSessionInfo Session(uint pid, string name, float vol, string device, string? path = DefaultExePath) =>
        new(SessionId(pid, name, device), vol, false, path);

    private sealed class EndpointSelectorMock : IAudioEndpointSelector
    {
        public string? EndpointId { get; set; } = DefaultEndpoint;
        public bool ThrowOnCall { get; set; }
        public string? GetDefaultMultimediaEndpointId()
        {
            if (ThrowOnCall)
                throw new InvalidOperationException("selector failure");
            return EndpointId;
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

    private static string CreateTempObligationRepo(out RestorationObligationRepository repo)
    {
        var dir = Path.Combine(Path.GetTempPath(), "vd_console_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var filePath = Path.Combine(dir, "obligations.json");
        repo = new RestorationObligationRepository(filePath);
        return dir;
    }

    [Fact]
    public void Factory_with_ConsoleLogger_reports_endpoint_selection()
    {
        using var sw = new StringWriter();
        var writer = new WriterMock();
        var store = new ApplicationVolumeStateStore();
        var classifier = new DuckingSessionClassifier();
        var selector = new EndpointSelectorMock { EndpointId = DefaultEndpoint };
        var tempDir = CreateTempObligationRepo(out var repo);
        try
        {
            var service = DuckingServiceFactory.Create(
                writer, classifier, store, repo, selector,
                errorWriter: sw);

            var sessions = new[]
            {
                Session(200, "Chrome.exe", 1.0f, DefaultEndpoint),
                Session(201, "Chrome.exe", 0.5f, NonDefaultEndpoint),
            };
            service.ApplyDucking(sessions, DefaultSettings, "VoiceDuck.exe");

            var output = sw.ToString();
            Assert.Contains("selected=true reason=default_multimedia", output);
            Assert.Contains("selected=false reason=not_default_multimedia", output);
            Assert.Contains(DefaultEndpoint, output);
            Assert.Contains(NonDefaultEndpoint, output);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Factory_with_ConsoleLogger_reports_lookup_failure()
    {
        using var sw = new StringWriter();
        var writer = new WriterMock();
        var store = new ApplicationVolumeStateStore();
        var classifier = new DuckingSessionClassifier();
        var selector = new EndpointSelectorMock { EndpointId = null };
        var tempDir = CreateTempObligationRepo(out var repo);
        try
        {
            var service = DuckingServiceFactory.Create(
                writer, classifier, store, repo, selector,
                errorWriter: sw);

            service.ApplyDucking(Array.Empty<AudioSessionInfo>(), DefaultSettings, "VoiceDuck.exe");

            var output = sw.ToString();
            Assert.Contains("reason=lookup_failed", output);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }
}
