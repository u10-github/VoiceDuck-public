namespace VoiceDuck.Core;

public record ExcludeApp
{
    public string ProcessName { get; }

    public ExcludeApp(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
            throw new ArgumentException("Process name must not be empty", nameof(processName));
        ProcessName = processName;
    }
}
