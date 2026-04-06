using System.Windows.Forms;

namespace VoiceInputApp.Services.Notification;

public class TrayNotificationService : INotificationService
{
    private readonly NotifyIcon _notifyIcon;

    public TrayNotificationService(NotifyIcon notifyIcon)
    {
        _notifyIcon = notifyIcon;
    }

    public void Show(string title, string message, NotificationType type = NotificationType.Info)
    {
        var icon = type switch
        {
            NotificationType.Error => ToolTipIcon.Error,
            NotificationType.Warning => ToolTipIcon.Warning,
            _ => ToolTipIcon.Info
        };

        _notifyIcon.ShowBalloonTip(3000, title, message, icon);
    }
}
