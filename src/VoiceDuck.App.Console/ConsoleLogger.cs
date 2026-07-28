using VoiceDuck.Core;

namespace VoiceDuck.App.Console;

public sealed class ConsoleLogger : ILogger
{
    private readonly TextWriter _writer;

    public ConsoleLogger() : this(System.Console.Error)
    {
    }

    public ConsoleLogger(TextWriter writer)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    public void Info(string message) => _writer.WriteLine("INFO: " + message);
    public void Warn(string message) => _writer.WriteLine("WARN: " + message);
    public void Error(string message) => _writer.WriteLine("ERROR: " + message);
}
