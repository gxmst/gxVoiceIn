namespace VoiceInputApp.Services.Hotkey;

public class HotkeyEventArgs : EventArgs
{
    public bool IsTriggerKey { get; set; }
    public bool IsKeyDown { get; set; }
}

public interface IHotkeyMonitor
{
    event EventHandler<HotkeyEventArgs>? KeyPressed;
    event EventHandler<HotkeyEventArgs>? KeyReleased;
    void Start();
    void Stop();
    void UpdateTriggerKey(int key);
}
