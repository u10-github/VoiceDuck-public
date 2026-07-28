namespace VoiceDuck.Core;

public enum ControlEligibilityRejectionReason
{
    UnresolvedIdentity,
    InvalidProcessId,
    MissingProcessName,
    MissingExecutablePath,
    IrrelevantEndpoint,
    TriggerApplication,
    Self,
    UserExcluded
}

public abstract record ControlEligibilityResult
{
    private ControlEligibilityResult() { }

    public sealed record Eligible : ControlEligibilityResult;

    public sealed record Rejected : ControlEligibilityResult
    {
        public ControlEligibilityRejectionReason Reason { get; }

        public Rejected(ControlEligibilityRejectionReason reason)
        {
            Reason = reason;
        }
    }
}
