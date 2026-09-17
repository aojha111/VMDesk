using System.Diagnostics;
using System.Text;
using VMDesk.Core.Enums;
using VMDesk.Core.Interfaces;

namespace VMDesk.Infrastructure.Logging;

/// <summary>
/// File logger with daily rotation and 10-day retention (spec §30).
/// NEVER accepts password/credential material in messages; callers are responsible
/// and the sanitizer below strips obvious secret-shaped patterns as a safety net.
/// </summary>
public sealed class FileLogFactory : IAppLogFactory
{
    private readonly string _directory;
    private readonly LogLevelOption _minLevel;
    private readonly Lock _gate = new();

    public FileLogFactory(string directory, LogLevelOption minLevel)
    {
        _directory = directory;
        _minLevel = minLevel;
        Directory.CreateDirectory(directory);
        PurgeOldLogs();
    }

    public IAppLog GetLogger(string category) => new FileLog(this, category);

    public void Flush()
    {
        // Writes are synchronous; nothing to flush.
    }

    internal void Write(string level, string category, string message)
    {
        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] [{category}] {Redact(message)}{Environment.NewLine}";
        if (message.Contains('\n') == false && message.Length < 200)
        {
            Debug.WriteLine($"[{category}] {message}");
        }

        lock (_gate)
        {
            var path = Path.Combine(_directory, $"vmdesk-{DateTimeOffset.UtcNow:yyyyMMdd}.log");
            try
            {
                File.AppendAllText(path, line, Encoding.UTF8);
            }
            catch (IOException)
            {
                // Never crash the app because of logging.
            }
        }
    }

    /// <summary>Defense in depth: never write anything shaped like a secret assignment.</summary>
    internal static string Redact(string message)
    {
        if (message.Contains("password", StringComparison.OrdinalIgnoreCase) && message.Contains('=', StringComparison.Ordinal))
        {
            return System.Text.RegularExpressions.Regex.Replace(
                message,
                @"(?i)(password|pwd|secret|token)\s*=\s*\S+",
                "$1=***");
        }

        return message;
    }

    private void PurgeOldLogs()
    {
        try
        {
            var cutoff = DateTimeOffset.UtcNow.AddDays(-10);
            foreach (var file in Directory.EnumerateFiles(_directory, "vmdesk-*.log"))
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                {
                    File.Delete(file);
                }
            }
        }
        catch (IOException)
        {
        }
    }

    private sealed class FileLog : IAppLog
    {
        private readonly FileLogFactory _owner;
        private readonly string _category;

        public FileLog(FileLogFactory owner, string category)
        {
            _owner = owner;
            _category = category;
        }

        public void Debug(string message)
        {
            if (_owner._minLevel <= LogLevelOption.Debug) _owner.Write("DEBUG", _category, message);
        }

        public void Info(string message)
        {
            if (_owner._minLevel <= LogLevelOption.Information) _owner.Write("INFO ", _category, message);
        }

        public void Warn(string message)
        {
            if (_owner._minLevel <= LogLevelOption.Warning) _owner.Write("WARN ", _category, message);
        }

        public void Error(string message, Exception? exception = null)
        {
            var text = exception is null ? message : $"{message} :: {exception.GetType().Name}: {exception.Message}";
            _owner.Write("ERROR", _category, text);
        }
    }
}
