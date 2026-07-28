using VoiceDuck.Core;

namespace VoiceDuck.Infrastructure.Tests;

public class RestorationObligationRepositoryTests
{
    private const int SchemaVersion = RestorationObligation.CurrentSchemaVersion;

    private static string GetTempFilePath()
    {
        return Path.Combine(Path.GetTempPath(), $"voiceduck-restoration-{Guid.NewGuid()}.json");
    }

    [Fact]
    public void Save_and_load_roundtrip()
    {
        var path = GetTempFilePath();
        try
        {
            var repo = new RestorationObligationRepository(path);
            var identity = new ApplicationAudioIdentity("device-1", @"C:\Games\GGST.exe");
            var created = DateTimeOffset.UtcNow.AddMinutes(-5);
            var updated = DateTimeOffset.UtcNow;
            var original = new RestorationObligation(
                identity,
                0.75f,
                RestorationStatus.Ducked,
                created,
                updated);

            repo.SaveAll(new[] { original });

            var result = repo.LoadAll();
            Assert.False(result.WasCorrupt);
            Assert.Single(result.Obligations);
            Assert.Equal(identity, result.Obligations[0].Identity);
            Assert.Equal(0.75f, result.Obligations[0].BaselineVolume, 3);
            Assert.Equal(RestorationStatus.Ducked, result.Obligations[0].Status);
            Assert.Equal(created, result.Obligations[0].CreatedAt);
            Assert.Equal(updated, result.Obligations[0].UpdatedAt);
            Assert.Equal(SchemaVersion, result.Obligations[0].SchemaVersion);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void LoadAll_returns_empty_when_file_missing()
    {
        var path = GetTempFilePath();

        var repo = new RestorationObligationRepository(path);
        var result = repo.LoadAll();

        Assert.False(result.WasCorrupt);
        Assert.Empty(result.Obligations);
    }

    [Fact]
    public void LoadAll_returns_empty_when_file_corrupt()
    {
        var path = GetTempFilePath();
        try
        {
            File.WriteAllText(path, "this is not valid json");

            var repo = new RestorationObligationRepository(path);
            var result = repo.LoadAll();

            Assert.True(result.WasCorrupt);
            Assert.Empty(result.Obligations);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void LoadAll_returns_empty_and_not_corrupt_for_empty_obligations_array()
    {
        var path = GetTempFilePath();
        try
        {
            var json = @"
{
  ""schemaVersion"": 1,
  ""obligations"": []
}
";
            File.WriteAllText(path, json);

            var repo = new RestorationObligationRepository(path);
            var result = repo.LoadAll();

            Assert.False(result.WasCorrupt);
            Assert.Empty(result.Obligations);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void DeleteAll_removes_file_and_is_idempotent()
    {
        var path = GetTempFilePath();
        try
        {
            var repo = new RestorationObligationRepository(path);
            var obligation = new RestorationObligation(
                new ApplicationAudioIdentity("device-1", @"C:\Games\GGST.exe"),
                0.75f,
                RestorationStatus.Ducked,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow);

            repo.SaveAll(new[] { obligation });
            Assert.True(File.Exists(path));

            repo.DeleteAll();
            Assert.False(File.Exists(path));
            Assert.Empty(repo.LoadAll().Obligations);

            repo.DeleteAll();
            Assert.False(File.Exists(path));
            Assert.Empty(repo.LoadAll().Obligations);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void SaveAll_with_empty_list_clears_obligations()
    {
        var path = GetTempFilePath();
        try
        {
            var repo = new RestorationObligationRepository(path);
            var identity = new ApplicationAudioIdentity("device-1", @"C:\Games\GGST.exe");
            var original = new RestorationObligation(
                identity,
                0.75f,
                RestorationStatus.Ducked,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow);

            repo.SaveAll(new[] { original });
            Assert.Single(repo.LoadAll().Obligations);

            repo.SaveAll(Array.Empty<RestorationObligation>());
            Assert.Empty(repo.LoadAll().Obligations);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Save_creates_directory_if_not_exists()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var path = Path.Combine(dir, "pending-restores.json");
        try
        {
            var repo = new RestorationObligationRepository(path);
            var identity = new ApplicationAudioIdentity("device-1", @"C:\Games\GGST.exe");
            var obligation = new RestorationObligation(
                identity,
                0.5f,
                RestorationStatus.RestorePending,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow);

            repo.SaveAll(new[] { obligation });

            Assert.True(Directory.Exists(dir));
            Assert.True(File.Exists(path));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void LoadAll_skips_obligations_with_unresolved_identity()
    {
        var path = GetTempFilePath();
        try
        {
            var json = @"
{
  ""schemaVersion"": $SCHEMA$,
  ""obligations"": [
    {
      ""identity"": { ""renderDeviceId"": ""device-1"", ""executablePath"": ""C:\\Games\\GGST.exe"" },
      ""baselineVolume"": 0.5,
      ""status"": ""Ducked"",
      ""createdAt"": ""2026-07-21T10:00:00+00:00"",
      ""updatedAt"": ""2026-07-21T10:00:00+00:00"",
      ""schemaVersion"": $SCHEMA$
    },
    {
      ""identity"": { ""renderDeviceId"": """", ""executablePath"": ""C:\\Games\\Invalid.exe"" },
      ""baselineVolume"": 0.5,
      ""status"": ""Ducked"",
      ""createdAt"": ""2026-07-21T10:00:00+00:00"",
      ""updatedAt"": ""2026-07-21T10:00:00+00:00"",
      ""schemaVersion"": $SCHEMA$
    }
  ]
}
".Replace("$SCHEMA$", SchemaVersion.ToString());
            File.WriteAllText(path, json);

            var repo = new RestorationObligationRepository(path);
            var result = repo.LoadAll();

            Assert.True(result.WasCorrupt);
            Assert.Single(result.Obligations);
            Assert.Equal("device-1", result.Obligations[0].Identity.RenderDeviceId);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void LoadAll_reports_corrupt_when_any_record_has_unknown_status()
    {
        var path = GetTempFilePath();
        try
        {
            var json = @"
{
  ""schemaVersion"": $SCHEMA$,
  ""obligations"": [
    {
      ""identity"": { ""renderDeviceId"": ""device-1"", ""executablePath"": ""C:\\Games\\GGST.exe"" },
      ""baselineVolume"": 0.5,
      ""status"": ""UnknownStatus"",
      ""createdAt"": ""2026-07-21T10:00:00+00:00"",
      ""updatedAt"": ""2026-07-21T10:00:00+00:00"",
      ""schemaVersion"": $SCHEMA$
    }
  ]
}
".Replace("$SCHEMA$", SchemaVersion.ToString());
            File.WriteAllText(path, json);

            var repo = new RestorationObligationRepository(path);
            var result = repo.LoadAll();

            Assert.True(result.WasCorrupt);
            Assert.Empty(result.Obligations);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Theory]
    [InlineData("99")]
    [InlineData("0")]
    [InlineData("1")]
    public void LoadAll_reports_corrupt_when_any_record_has_numeric_status(string statusValue)
    {
        var path = GetTempFilePath();
        try
        {
            var json = @"
{
  ""schemaVersion"": $SCHEMA$,
  ""obligations"": [
    {
      ""identity"": { ""renderDeviceId"": ""device-1"", ""executablePath"": ""C:\\Games\\GGST.exe"" },
      ""baselineVolume"": 0.5,
      ""status"": ""$STATUS$"",
      ""createdAt"": ""2026-07-21T10:00:00+00:00"",
      ""updatedAt"": ""2026-07-21T10:00:00+00:00"",
      ""schemaVersion"": $SCHEMA$
    }
  ]
}
".Replace("$SCHEMA$", SchemaVersion.ToString()).Replace("$STATUS$", statusValue);
            File.WriteAllText(path, json);

            var repo = new RestorationObligationRepository(path);
            var result = repo.LoadAll();

            Assert.True(result.WasCorrupt);
            Assert.Empty(result.Obligations);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void LoadAll_reports_corrupt_when_top_level_schema_version_is_unsupported()
    {
        var path = GetTempFilePath();
        try
        {
            var json = @"
{
  ""schemaVersion"": 999,
  ""obligations"": [
    {
      ""identity"": { ""renderDeviceId"": ""device-1"", ""executablePath"": ""C:\\Games\\GGST.exe"" },
      ""baselineVolume"": 0.5,
      ""status"": ""Ducked"",
      ""createdAt"": ""2026-07-21T10:00:00+00:00"",
      ""updatedAt"": ""2026-07-21T10:00:00+00:00"",
      ""schemaVersion"": 999
    }
  ]
}
";
            File.WriteAllText(path, json);

            var repo = new RestorationObligationRepository(path);
            var result = repo.LoadAll();

            Assert.True(result.WasCorrupt);
            Assert.Empty(result.Obligations);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void LoadAll_reports_corrupt_when_any_record_has_unsupported_schema_version()
    {
        var path = GetTempFilePath();
        try
        {
            var json = @"
{
  ""schemaVersion"": $SCHEMA$,
  ""obligations"": [
    {
      ""identity"": { ""renderDeviceId"": ""device-1"", ""executablePath"": ""C:\\Games\\GGST.exe"" },
      ""baselineVolume"": 0.5,
      ""status"": ""Ducked"",
      ""createdAt"": ""2026-07-21T10:00:00+00:00"",
      ""updatedAt"": ""2026-07-21T10:00:00+00:00"",
      ""schemaVersion"": 999
    }
  ]
}
".Replace("$SCHEMA$", SchemaVersion.ToString());
            File.WriteAllText(path, json);

            var repo = new RestorationObligationRepository(path);
            var result = repo.LoadAll();

            Assert.True(result.WasCorrupt);
            Assert.Empty(result.Obligations);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void LoadAll_reports_corrupt_when_obligations_field_is_missing()
    {
        var path = GetTempFilePath();
        try
        {
            var json = @"
{
  ""schemaVersion"": 1
}
";
            File.WriteAllText(path, json);

            var repo = new RestorationObligationRepository(path);
            var result = repo.LoadAll();

            Assert.True(result.WasCorrupt);
            Assert.Empty(result.Obligations);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void SaveAll_preserves_multiple_obligations()
    {
        var path = GetTempFilePath();
        try
        {
            var repo = new RestorationObligationRepository(path);
            var now = DateTimeOffset.UtcNow;
            var obligations = new[]
            {
                new RestorationObligation(
                    new ApplicationAudioIdentity("device-a", @"C:\Games\GGST.exe"),
                    1.0f,
                    RestorationStatus.Ducked,
                    now,
                    now),
                new RestorationObligation(
                    new ApplicationAudioIdentity("device-b", @"C:\Apps\Chrome.exe"),
                    0.8f,
                    RestorationStatus.RestorePending,
                    now,
                    now),
            };

            repo.SaveAll(obligations);
            var result = repo.LoadAll();

            Assert.Equal(2, result.Obligations.Count);
            Assert.Contains(result.Obligations, o =>
                o.Identity.RenderDeviceId == "device-a" &&
                o.Identity.ExecutablePath.Equals(@"C:\Games\GGST.exe", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Obligations, o =>
                o.Identity.RenderDeviceId == "device-b" &&
                o.Status == RestorationStatus.RestorePending);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void SaveAll_rejects_unsupported_schema_version()
    {
        var path = GetTempFilePath();
        try
        {
            var repo = new RestorationObligationRepository(path);
            var obligation = new RestorationObligation(
                new ApplicationAudioIdentity("device-1", @"C:\Games\GGST.exe"),
                0.5f,
                RestorationStatus.Ducked,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                SchemaVersion: 999);

            Assert.Throws<ArgumentException>(() => repo.SaveAll(new[] { obligation }));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void SaveAll_rejects_unresolved_identity()
    {
        var path = GetTempFilePath();
        try
        {
            var repo = new RestorationObligationRepository(path);
            var obligation = new RestorationObligation(
                new ApplicationAudioIdentity("", @""),
                0.5f,
                RestorationStatus.Ducked,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow);

            Assert.Throws<ArgumentException>(() => repo.SaveAll(new[] { obligation }));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Theory]
    [InlineData("baselineVolume")]
    [InlineData("createdAt")]
    [InlineData("updatedAt")]
    public void LoadAll_reports_corrupt_when_required_field_is_missing(string missingField)
    {
        var path = GetTempFilePath();
        try
        {
            var json = @"
{
  ""schemaVersion"": $SCHEMA$,
  ""obligations"": [
    {
      ""identity"": { ""renderDeviceId"": ""device-1"", ""executablePath"": ""C:\\Games\\GGST.exe"" },
      ""baselineVolume"": 0.5,
      ""status"": ""Ducked"",
      ""createdAt"": ""2026-07-21T10:00:00+00:00"",
      ""updatedAt"": ""2026-07-21T10:00:00+00:00"",
      ""schemaVersion"": $SCHEMA$
    }
  ]
}
".Replace("$SCHEMA$", SchemaVersion.ToString());

            var fieldName = missingField.ToLowerInvariant();
            var modifiedJson = fieldName switch
            {
                "baselinevolume" => json.Replace("\"baselineVolume\": 0.5,", string.Empty),
                "createdat" => json.Replace("\"createdAt\": \"2026-07-21T10:00:00+00:00\",", string.Empty),
                "updatedat" => json.Replace("\"updatedAt\": \"2026-07-21T10:00:00+00:00\",", string.Empty),
                _ => json,
            };

            File.WriteAllText(path, modifiedJson);

            var repo = new RestorationObligationRepository(path);
            var result = repo.LoadAll();

            Assert.True(result.WasCorrupt);
            Assert.Empty(result.Obligations);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Theory]
    [InlineData(-0.1f)]
    [InlineData(1.1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void SaveAll_rejects_out_of_range_baseline_volume(float baselineVolume)
    {
        var path = GetTempFilePath();
        try
        {
            var repo = new RestorationObligationRepository(path);
            var obligation = new RestorationObligation(
                new ApplicationAudioIdentity("device-1", @"C:\Games\GGST.exe"),
                baselineVolume,
                RestorationStatus.Ducked,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow);

            Assert.Throws<ArgumentException>(() => repo.SaveAll(new[] { obligation }));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Theory]
    [InlineData("-0.1")]
    [InlineData("1.1")]
    [InlineData("null")]
    public void LoadAll_reports_corrupt_when_baseline_volume_is_out_of_range_or_null(string baselineValue)
    {
        var path = GetTempFilePath();
        try
        {
            var json = @"
{
  ""schemaVersion"": $SCHEMA$,
  ""obligations"": [
    {
      ""identity"": { ""renderDeviceId"": ""device-1"", ""executablePath"": ""C:\\Games\\GGST.exe"" },
      ""baselineVolume"": $BASELINE$,
      ""status"": ""Ducked"",
      ""createdAt"": ""2026-07-21T10:00:00+00:00"",
      ""updatedAt"": ""2026-07-21T10:00:00+00:00"",
      ""schemaVersion"": $SCHEMA$
    }
  ]
}
".Replace("$SCHEMA$", SchemaVersion.ToString()).Replace("$BASELINE$", baselineValue);

            File.WriteAllText(path, json);

            var repo = new RestorationObligationRepository(path);
            var result = repo.LoadAll();

            Assert.True(result.WasCorrupt);
            Assert.Empty(result.Obligations);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
