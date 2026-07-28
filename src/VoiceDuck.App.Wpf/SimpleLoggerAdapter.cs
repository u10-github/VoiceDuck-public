using VoiceDuck.Core;

namespace VoiceDuck.App.Wpf;

internal sealed class SimpleLoggerAdapter : ILogger
{
    private readonly SimpleLogger _inner;

    public SimpleLoggerAdapter(SimpleLogger inner)
    {
        _inner = inner;
    }

    public void Info(string message) => _inner.Info(message);
    public void Warn(string message) => _inner.Warn(message);
    public void Error(string message) => _inner.Error(message);
}
