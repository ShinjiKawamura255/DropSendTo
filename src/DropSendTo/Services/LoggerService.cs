using System;
using System.IO;
using System.Text;

namespace DropSendTo.Services;

public class LoggerService : IAppLogger
{
    private static readonly Lazy<LoggerService> _lazy = new(() => new LoggerService());
    public static LoggerService Instance => _lazy.Value;

    private const long DefaultMaxLogBytes = 1_000_000;
    private const int RetentionDays = 7;
    private readonly string _logDir;
    private readonly string _logPath;
    private readonly Func<DateTime> _localNow;
    private readonly Func<DateTime> _utcNow;
    private readonly long _maxLogBytes;
    private readonly TimeSpan _retentionPeriod = TimeSpan.FromDays(RetentionDays);
    private readonly object _lock = new();

    public string LogDirectory => _logDir;

    private LoggerService()
        : this(
            Path.Combine(
                AppDataPathResolver.ResolveBaseDirectory(),
                "DropSendTo",
                "logs"),
            () => DateTime.Now,
            () => DateTime.UtcNow,
            DefaultMaxLogBytes)
    {
    }

    internal LoggerService(
        string logDirectory,
        Func<DateTime> localNow,
        Func<DateTime> utcNow,
        long maxLogBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logDirectory);
        ArgumentNullException.ThrowIfNull(localNow);
        ArgumentNullException.ThrowIfNull(utcNow);
        ArgumentOutOfRangeException.ThrowIfNegative(maxLogBytes);

        _logDir = logDirectory;
        _localNow = localNow;
        _utcNow = utcNow;
        _maxLogBytes = maxLogBytes;
        Directory.CreateDirectory(_logDir);
        _logPath = Path.Combine(_logDir, "app.log");
        CleanupOldLogs();
    }

    public void Info(string message) => Write("INFO", message);
    public void Warn(string message) => Write("WARN", message);
    public void Error(string message) => Write("ERROR", message);

    public void CleanupOldLogs()
    {
        try
        {
            if (!Directory.Exists(_logDir))
            {
                return;
            }

            var cutoff = _utcNow() - _retentionPeriod;
            foreach (var file in Directory.GetFiles(_logDir, "app*.log"))
            {
                try
                {
                    var lastWrite = File.GetLastWriteTimeUtc(file);
                    if (lastWrite < cutoff)
                    {
                        File.Delete(file);
                    }
                }
                catch
                {
                    // ignore deletion failures
                }
            }
        }
        catch
        {
            // swallow cleanup errors
        }
    }

    private void Write(string level, string message)
    {
        try
        {
            var now = _localNow();
            var line = $"{now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";
            lock (_lock)
            {
                if (File.Exists(_logPath) && new FileInfo(_logPath).Length > _maxLogBytes)
                {
                    TryRotate(now);
                }

                File.AppendAllText(_logPath, line, Encoding.UTF8);
            }
        }
        catch
        {
            // swallow logging errors
        }
    }

    private void TryRotate(DateTime now)
    {
        try
        {
            var timestamp = now.ToString("yyyyMMddHHmmss");
            var archivePath = Path.Combine(_logDir, $"app-{timestamp}.log");
            var suffix = 1;
            while (File.Exists(archivePath))
            {
                archivePath = Path.Combine(_logDir, $"app-{timestamp}-{suffix}.log");
                suffix++;
            }

            File.Move(_logPath, archivePath, overwrite: false);
        }
        catch
        {
            // Rotation failure must not prevent appending to a still-writable current log.
        }
    }
}
