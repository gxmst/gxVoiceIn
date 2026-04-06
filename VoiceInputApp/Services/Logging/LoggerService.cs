using System.IO;

namespace VoiceInputApp.Services.Logging;

public class LoggerService : ILoggerService
{
    private readonly string _logDirectory;
    private readonly object _lock = new();

    public LoggerService()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _logDirectory = Path.Combine(appDataPath, "VoiceInput", "logs");
        Directory.CreateDirectory(_logDirectory);
    }

    public ILogger GetLogger(string name)
    {
        return new FileLogger(name, _logDirectory, _lock);
    }

    private class FileLogger : ILogger
    {
        private readonly string _name;
        private readonly string _logDirectory;
        private readonly object _lock;

        public FileLogger(string name, string logDirectory, object lockObj)
        {
            _name = name;
            _logDirectory = logDirectory;
            _lock = lockObj;
        }

        public void Debug(string message) => WriteLog("DEBUG", message);
        public void Info(string message) => WriteLog("INFO", message);
        public void Warning(string message) => WriteLog("WARN", message);
        public void Error(string message, Exception? exception = null)
        {
            var fullMessage = exception != null
                ? $"{message}\nException: {exception.Message}\n{exception.StackTrace}"
                : message;
            WriteLog("ERROR", fullMessage);
        }

        private void WriteLog(string level, string message)
        {
            lock (_lock)
            {
                var logFile = Path.Combine(_logDirectory, $"app-{DateTime.Now:yyyy-MM-dd}.log");
                var logLine = $"[{DateTime.Now:HH:mm:ss.fff}] [{level}] [{_name}] {message}\n";
                File.AppendAllText(logFile, logLine);
            }
        }
    }
}
