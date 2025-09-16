using System;
using System.IO;
using System.Text;

namespace DropSendTo.Services;

public class LoggerService
{
    private static readonly Lazy<LoggerService> _lazy = new(() => new LoggerService());
    public static LoggerService Instance => _lazy.Value;

    private readonly string _logDir;
    private readonly string _logPath;
    private readonly object _lock = new();

    private LoggerService()
    {
        var baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DropSendTo");
        _logDir = Path.Combine(baseDir, "logs");
        Directory.CreateDirectory(_logDir);
        _logPath = Path.Combine(_logDir, "app.log");
    }

    public void Info(string message) => Write("INFO", message);
    public void Warn(string message) => Write("WARN", message);
    public void Error(string message) => Write("ERROR", message);

    private void Write(string level, string message)
    {
        try
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";
            lock (_lock)
            {
                // naive rotation ~1MB
                if (File.Exists(_logPath) && new FileInfo(_logPath).Length > 1_000_000)
                {
                    var bak = Path.Combine(_logDir, $"app-{DateTime.Now:yyyyMMddHHmmss}.log");
                    File.Move(_logPath, bak, overwrite: false);
                }
                File.AppendAllText(_logPath, line, Encoding.UTF8);
            }
        }
        catch
        {
            // swallow logging errors
        }
    }
}

