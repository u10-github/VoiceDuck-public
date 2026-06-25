namespace VoiceDuck.Core;

public class DuckingSessionClassifier
{
    public DuckingTargetDecision Classify(
        AudioSessionInfo session,
        VoiceDuckSettings settings,
        string voiceDuckProcessName)
    {
        var processName = session.Identity.ProcessName;

        if (string.Equals(processName, voiceDuckProcessName, StringComparison.OrdinalIgnoreCase))
            return DuckingTargetDecision.Protect($"{processName} is VoiceDuck itself");

        foreach (var trigger in settings.TriggerApps)
        {
            if (!trigger.Enabled)
                continue;
            if (string.Equals(processName, trigger.ProcessName, StringComparison.OrdinalIgnoreCase))
                return DuckingTargetDecision.Protect($"{processName} is a trigger app");
        }

        foreach (var exclude in settings.ExcludeApps)
        {
            if (string.Equals(processName, exclude.ProcessName, StringComparison.OrdinalIgnoreCase))
                return DuckingTargetDecision.Protect($"{processName} is an exclude app");
        }

        return DuckingTargetDecision.Duck($"{processName} is not a trigger or exclude app");
    }
}
