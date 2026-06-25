using System.IO;

namespace VoiceDuck.App.Wpf;

public sealed class SimpleLogger : IDisposable
{
    public enum Level { Info, Warning, Error }

    public string LogDirectory { get; }

    private readonly string _filePath;
    private readonly object _lock = new();
    private StreamWriter? _writer;
    private string _currentDate;

    public SimpleLogger(string directoryPath)
    {
        LogDirectory = directoryPath;
        Directory.CreateDirectory(directoryPath);
        _currentDate = DateTime.Now.ToString("yyyy-MM-dd");
        _filePath = Path.Combine(directoryPath, $"voiceduck-{_currentDate}.log");
        _writer = new StreamWriter(_filePath, append: true) { AutoFlush = true };
    }

    public void Info(string message) => Write(Level.Info, message);
    public void Warn(string message) => Write(Level.Warning, message);
    public void Error(string message) => Write(Level.Error, message);
    public void Error(string message, Exception ex) => Write(Level.Error, $"{message}: {ex}");

    private void Write(Level level, string message)
    {
        var date = DateTime.Now;
        var dateStr = date.ToString("yyyy-MM-dd");

        lock (_lock)
        {
            if (_writer == null)
                return;

            if (_currentDate != dateStr)
            {
                _writer.Dispose();
                _currentDate = dateStr;
                var newPath = Path.Combine(LogDirectory, $"voiceduck-{dateStr}.log");
                _writer = new StreamWriter(newPath, append: true) { AutoFlush = true };
            }

            _writer.WriteLine($"{date:yyyy-MM-dd HH:mm:ss} [{level}] {message}");
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }
}
