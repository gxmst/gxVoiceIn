namespace VoiceInputApp.Services.Logging;

public interface ILogger
{
    void Debug(string message);
    void Info(string message);
    void Warning(string message);
    void Error(string message, Exception? exception = null);
}

public interface ILoggerService
{
    ILogger GetLogger(string name);
}
