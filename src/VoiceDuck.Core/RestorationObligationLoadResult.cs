namespace VoiceDuck.Core;

public sealed record RestorationObligationLoadResult(
    IReadOnlyList<RestorationObligation> Obligations,
    bool WasCorrupt);
