namespace VoiceInputApp.Services.Startup;

public interface IAutoStartService
{
    bool IsEnabled();
    void SetEnabled(bool enabled);
}
