namespace VoiceDuck.Core;

public enum DuckingOutcome
{
    Duck,
    Protect
}

public record DuckingTargetDecision
{
    public DuckingOutcome Outcome { get; }
    public string Reason { get; }

    private DuckingTargetDecision(DuckingOutcome outcome, string reason)
    {
        Outcome = outcome;
        Reason = reason ?? throw new ArgumentNullException(nameof(reason));
    }

    public static DuckingTargetDecision Duck(string reason) =>
        new(DuckingOutcome.Duck, reason);

    public static DuckingTargetDecision Protect(string reason) =>
        new(DuckingOutcome.Protect, reason);
}
