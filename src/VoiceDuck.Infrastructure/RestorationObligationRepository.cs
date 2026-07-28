using System.Text.Json;
using VoiceDuck.Core;

namespace VoiceDuck.Infrastructure;

public sealed class RestorationObligationRepository : IRestorationObligationRepository
{
    private readonly string _filePath;
    private readonly object _gate = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public RestorationObligationRepository(string filePath)
    {
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
    }

    public RestorationObligationLoadResult LoadAll()
    {
        lock (_gate)
        {
            if (!File.Exists(_filePath))
            {
                return new RestorationObligationLoadResult(
                    Array.Empty<RestorationObligation>(),
                    WasCorrupt: false);
            }

            try
            {
                var json = File.ReadAllText(_filePath);
                var dto = JsonSerializer.Deserialize<RestorationObligationListDto>(json, JsonOptions);
                var (obligations, wasCorrupt) = ToDomainList(dto);
                return new RestorationObligationLoadResult(obligations, wasCorrupt);
            }
            catch
            {
                return new RestorationObligationLoadResult(
                    Array.Empty<RestorationObligation>(),
                    WasCorrupt: true);
            }
        }
    }

    public void SaveAll(IReadOnlyList<RestorationObligation> obligations)
    {
        ArgumentNullException.ThrowIfNull(obligations);

        foreach (var obligation in obligations)
        {
            ValidateObligation(obligation);
        }

        lock (_gate)
        {
            var dto = ToDtoList(obligations);
            var json = JsonSerializer.Serialize(dto, JsonOptions);

            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var tempPath = _filePath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _filePath, overwrite: true);
        }
    }

    public void DeleteAll()
    {
        lock (_gate)
        {
            if (File.Exists(_filePath))
            {
                File.Delete(_filePath);
            }
        }
    }

    private static void ValidateObligation(RestorationObligation obligation)
    {
        ArgumentNullException.ThrowIfNull(obligation);

        if (obligation.SchemaVersion != RestorationObligation.CurrentSchemaVersion)
        {
            throw new ArgumentException(
                $"Schema version {obligation.SchemaVersion} is not supported. Expected {RestorationObligation.CurrentSchemaVersion}.",
                nameof(obligation));
        }

        if (!Enum.IsDefined(obligation.Status))
        {
            throw new ArgumentException(
                $"Status '{obligation.Status}' is not a defined {nameof(RestorationStatus)} value.",
                nameof(obligation));
        }

        if (!obligation.Identity.IsResolved)
        {
            throw new ArgumentException(
                "Obligation identity must be resolved with non-empty render device and executable path.",
                nameof(obligation));
        }

        if (!IsValidBaselineVolume(obligation.BaselineVolume))
        {
            throw new ArgumentException(
                "BaselineVolume must be a finite value in the range [0, 1].",
                nameof(obligation));
        }
    }

    private static bool IsValidBaselineVolume(float volume)
    {
        return float.IsFinite(volume) && volume >= 0.0f && volume <= 1.0f;
    }

    private static bool TryParseStatus(string? value, out RestorationStatus status)
    {
        status = default;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        return value switch
        {
            nameof(RestorationStatus.Ducked)
                => Assign(RestorationStatus.Ducked, out status),

            nameof(RestorationStatus.RestorePending)
                => Assign(RestorationStatus.RestorePending, out status),

            _ => false,
        };
    }

    private static bool Assign(RestorationStatus value, out RestorationStatus target)
    {
        target = value;
        return true;
    }

    private static (IReadOnlyList<RestorationObligation> Obligations, bool WasCorrupt) ToDomainList(RestorationObligationListDto? dto)
    {
        if (dto is null || dto.Obligations is null)
        {
            return (Array.Empty<RestorationObligation>(), WasCorrupt: true);
        }

        if (dto.SchemaVersion != RestorationObligation.CurrentSchemaVersion)
        {
            return (Array.Empty<RestorationObligation>(), WasCorrupt: true);
        }

        var obligations = new List<RestorationObligation>();
        var wasCorrupt = false;

        foreach (var item in dto.Obligations)
        {
            if (item?.Identity is null
                || item.BaselineVolume is null
                || !IsValidBaselineVolume(item.BaselineVolume.Value)
                || item.Status is null
                || item.CreatedAt is null
                || item.UpdatedAt is null)
            {
                wasCorrupt = true;
                continue;
            }

            var identity = new ApplicationAudioIdentity(
                item.Identity.RenderDeviceId ?? string.Empty,
                item.Identity.ExecutablePath ?? string.Empty);

            if (!identity.IsResolved)
            {
                wasCorrupt = true;
                continue;
            }

            if (!TryParseStatus(item.Status, out var status))
            {
                wasCorrupt = true;
                continue;
            }

            if (item.SchemaVersion != RestorationObligation.CurrentSchemaVersion)
            {
                wasCorrupt = true;
                continue;
            }

            obligations.Add(new RestorationObligation(
                identity,
                item.BaselineVolume.Value,
                status,
                item.CreatedAt.Value,
                item.UpdatedAt.Value,
                item.SchemaVersion));
        }

        return (obligations, wasCorrupt);
    }

    private static RestorationObligationListDto ToDtoList(IReadOnlyList<RestorationObligation> obligations)
    {
        return new RestorationObligationListDto
        {
            SchemaVersion = RestorationObligation.CurrentSchemaVersion,
            Obligations = obligations.Select(o => new RestorationObligationDto
            {
                Identity = new ApplicationAudioIdentityDto
                {
                    RenderDeviceId = o.Identity.RenderDeviceId,
                    ExecutablePath = o.Identity.ExecutablePath,
                },
                BaselineVolume = o.BaselineVolume,
                Status = o.Status.ToString(),
                CreatedAt = o.CreatedAt,
                UpdatedAt = o.UpdatedAt,
                SchemaVersion = o.SchemaVersion,
            }).ToList(),
        };
    }

    private class RestorationObligationListDto
    {
        public int SchemaVersion { get; set; }
        public List<RestorationObligationDto>? Obligations { get; set; }
    }

    private class RestorationObligationDto
    {
        public ApplicationAudioIdentityDto? Identity { get; set; }
        public float? BaselineVolume { get; set; }
        public string? Status { get; set; }
        public DateTimeOffset? CreatedAt { get; set; }
        public DateTimeOffset? UpdatedAt { get; set; }
        public int SchemaVersion { get; set; }
    }

    private class ApplicationAudioIdentityDto
    {
        public string? RenderDeviceId { get; set; }
        public string? ExecutablePath { get; set; }
    }
}
