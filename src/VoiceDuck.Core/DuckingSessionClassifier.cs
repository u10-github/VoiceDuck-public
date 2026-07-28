namespace VoiceDuck.Core;

public class DuckingSessionClassifier
{
    public ControlEligibilityResult Classify(
        AudioSessionInfo session,
        string? relevantEndpointId,
        VoiceDuckSettings settings,
        string voiceDuckProcessName)
    {
        if (!session.Identity.IsResolved)
        {
            return new ControlEligibilityResult.Rejected(
                ControlEligibilityRejectionReason.UnresolvedIdentity);
        }

        if (session.Identity.ProcessId == 0)
        {
            return new ControlEligibilityResult.Rejected(
                ControlEligibilityRejectionReason.InvalidProcessId);
        }

        var processName = session.Identity.ProcessName;
        if (string.IsNullOrWhiteSpace(processName))
        {
            return new ControlEligibilityResult.Rejected(
                ControlEligibilityRejectionReason.MissingProcessName);
        }

        if (string.IsNullOrWhiteSpace(session.ExecutablePath))
        {
            return new ControlEligibilityResult.Rejected(
                ControlEligibilityRejectionReason.MissingExecutablePath);
        }

        if (string.IsNullOrWhiteSpace(relevantEndpointId)
            || !string.Equals(
                session.Identity.RenderDeviceId,
                relevantEndpointId,
                StringComparison.Ordinal))
        {
            return new ControlEligibilityResult.Rejected(
                ControlEligibilityRejectionReason.IrrelevantEndpoint);
        }

        foreach (var trigger in settings.TriggerApps)
        {
            if (!trigger.Enabled)
                continue;
            if (string.Equals(processName, trigger.ProcessName, StringComparison.OrdinalIgnoreCase))
            {
                return new ControlEligibilityResult.Rejected(
                    ControlEligibilityRejectionReason.TriggerApplication);
            }
        }

        if (string.Equals(processName, voiceDuckProcessName, StringComparison.OrdinalIgnoreCase))
        {
            return new ControlEligibilityResult.Rejected(
                ControlEligibilityRejectionReason.Self);
        }

        foreach (var exclude in settings.ExcludeApps)
        {
            if (string.Equals(processName, exclude.ProcessName, StringComparison.OrdinalIgnoreCase))
            {
                return new ControlEligibilityResult.Rejected(
                    ControlEligibilityRejectionReason.UserExcluded);
            }
        }

        return new ControlEligibilityResult.Eligible();
    }
}
