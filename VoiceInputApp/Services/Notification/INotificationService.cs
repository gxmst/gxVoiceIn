namespace VoiceInputApp.Services.Notification;

public enum NotificationType
{
    Info,
    Warning,
    Error
}

public interface INotificationService
{
    void Show(string title, string message, NotificationType type = NotificationType.Info);
}
