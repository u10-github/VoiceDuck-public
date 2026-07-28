namespace VoiceDuck.Core;

public abstract record BaselineSelectionResult
{
    public IReadOnlyList<float> Candidates { get; }
    public float Spread { get; }

    private protected BaselineSelectionResult(IReadOnlyList<float> candidates, float spread)
    {
        Candidates = candidates;
        Spread = spread;
    }

    public sealed record Selected : BaselineSelectionResult
    {
        public float Baseline { get; }

        internal Selected(IReadOnlyList<float> candidates, float spread, float baseline)
            : base(candidates, spread)
        {
            Baseline = baseline;
        }
    }

    public sealed record NoCandidates : BaselineSelectionResult
    {
        internal NoCandidates() : base(Array.Empty<float>(), 0f) { }
    }

    public sealed record Conflict : BaselineSelectionResult
    {
        internal Conflict(IReadOnlyList<float> candidates, float spread)
            : base(candidates, spread) { }
    }
}

public static class BaselineSelectionPolicy
{
    public const float ConflictTolerance = 0.01f;
    public const float ComparisonEpsilon = 0.000001f;

    public static BaselineSelectionResult Select(IEnumerable<float> candidateVolumes)
    {
        ArgumentNullException.ThrowIfNull(candidateVolumes);

        var candidates = candidateVolumes.ToArray();
        if (candidates.Length == 0)
            return new BaselineSelectionResult.NoCandidates();

        var minimum = candidates.Min();
        var maximum = candidates.Max();
        var spread = maximum - minimum;

        if (spread > ConflictTolerance + ComparisonEpsilon)
            return new BaselineSelectionResult.Conflict(candidates, spread);

        return new BaselineSelectionResult.Selected(candidates, spread, maximum);
    }
}
