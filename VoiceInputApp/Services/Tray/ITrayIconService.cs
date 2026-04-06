using System.Windows.Forms;

namespace VoiceInputApp.Services.Tray;

public interface ITrayIconService
{
    void Initialize();
    void Show();
    void Hide();
    void UpdateMenu();
    NotifyIcon GetNotifyIcon();
}
