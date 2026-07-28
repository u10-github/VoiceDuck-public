namespace VoiceDuck.Core;

public sealed record RestorationObligation(
    ApplicationAudioIdentity Identity,
    float BaselineVolume,
    RestorationStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int SchemaVersion = RestorationObligation.CurrentSchemaVersion)
{
    public const int CurrentSchemaVersion = 1;
}
