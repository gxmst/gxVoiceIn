using System.IO;

namespace VoiceInputApp.Services.Logging;

public interface ILoggingService
{
    void Debug(string message);
    void Info(string message);
    void Warning(string message);
    void Error(string message, Exception? exception = null);
    string GetLogFilePath();
    string GetRecentLogs(int lines = 100);
}

public class LoggingService : ILoggingService
{
    private const long MaxLogFileBytes = 2 * 1024 * 1024;
    private const int RetentionDays = 7;
    private readonly string _logFilePath;
    private readonly string _logDirectory;
    private readonly object _lock = new();
    private static ILoggingService? _instance;
    private readonly LogLevel _minimumLevel;

    public static ILoggingService Instance => _instance ??= new LoggingService();

    public LoggingService()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _logDirectory = Path.Combine(appDataPath, "VoiceInputApp", "logs");
        
        if (!Directory.Exists(_logDirectory))
        {
            Directory.CreateDirectory(_logDirectory);
        }

        _logFilePath = Path.Combine(_logDirectory, $"voiceinput_{DateTime.Now:yyyyMMdd}.log");
        _minimumLevel = ParseLogLevel(Environment.GetEnvironmentVariable("VOICEINPUT_LOG_LEVEL"));
        CleanupOldLogs();
    }

    public void Debug(string message)
    {
        WriteLog("DEBUG", message);
    }

    public void Info(string message)
    {
        WriteLog("INFO", message);
    }

    public void Warning(string message)
    {
        WriteLog("WARN", message);
    }

    public void Error(string message, Exception? exception = null)
    {
        var fullMessage = exception != null 
            ? $"{message}\nException: {exception.GetType().Name}: {exception.Message}\n{exception.StackTrace}"
            : message;
        WriteLog("ERROR", fullMessage);
    }

    private void WriteLog(string level, string message)
    {
        lock (_lock)
        {
            var logLevel = ParseLogLevel(level);
            if (logLevel < _minimumLevel)
            {
                return;
            }

            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var logLine = $"[{timestamp}] [{level}] {message}\n";
            
            try
            {
                TrimCurrentLogIfNeeded(logLine.Length * sizeof(char));
                File.AppendAllText(_logFilePath, logLine);
            }
            catch
            {
            }

            System.Diagnostics.Debug.WriteLine(logLine.TrimEnd('\n'));
        }
    }

    public string GetLogFilePath()
    {
        return _logFilePath;
    }

    public string GetRecentLogs(int lines = 100)
    {
        lock (_lock)
        {
            try
            {
                if (!File.Exists(_logFilePath))
                {
                    return "No log file found.";
                }

                var allLines = File.ReadAllLines(_logFilePath);
                var recentLines = allLines.TakeLast(lines);
                return string.Join("\n", recentLines);
            }
            catch (Exception ex)
            {
                return $"Error reading log: {ex.Message}";
            }
        }
    }

    private void CleanupOldLogs()
    {
        try
        {
            var cutoff = DateTime.Now.AddDays(-RetentionDays);
            foreach (var file in Directory.GetFiles(_logDirectory, "voiceinput_*.log"))
            {
                var lastWriteTime = File.GetLastWriteTime(file);
                if (lastWriteTime < cutoff)
                {
                    File.Delete(file);
                }
            }
        }
        catch
        {
        }
    }

    private void TrimCurrentLogIfNeeded(int incomingCharsEstimate)
    {
        try
        {
            if (!File.Exists(_logFilePath))
            {
                return;
            }

            var fileInfo = new FileInfo(_logFilePath);
            if (fileInfo.Length + incomingCharsEstimate <= MaxLogFileBytes)
            {
                return;
            }

            var content = File.ReadAllText(_logFilePath);
            var targetChars = Math.Max(content.Length / 2, 0);
            var startIndex = Math.Max(content.Length - targetChars, 0);
            var trimmed = content[startIndex..];
            var newlineIndex = trimmed.IndexOf('\n');
            if (newlineIndex >= 0 && newlineIndex < trimmed.Length - 1)
            {
                trimmed = trimmed[(newlineIndex + 1)..];
            }

            File.WriteAllText(_logFilePath, trimmed);
        }
        catch
        {
        }
    }

    private static LogLevel ParseLogLevel(string? value)
    {
        return value?.Trim().ToUpperInvariant() switch
        {
            "DEBUG" => LogLevel.Debug,
            "WARN" => LogLevel.Warning,
            "WARNING" => LogLevel.Warning,
            "ERROR" => LogLevel.Error,
            _ => LogLevel.Info
        };
    }

    private enum LogLevel
    {
        Debug = 0,
        Info = 1,
        Warning = 2,
        Error = 3
    }
}
