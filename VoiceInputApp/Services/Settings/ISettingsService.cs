using VoiceInputApp.Models;

namespace VoiceInputApp.Services.Settings;

public interface ISettingsService
{
    AppSettings Load();
    void Save(AppSettings settings);
    AppSettings Current { get; }
}
