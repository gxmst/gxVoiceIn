using System.IO;
using System.Text.Json;
using VoiceInputApp.Models;

namespace VoiceInputApp.Services.Settings;

public class SettingsService : ISettingsService
{
    private readonly string _settingsPath;
    private AppSettings _current;
    private readonly object _lock = new();

    public SettingsService()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var settingsDir = Path.Combine(appDataPath, "VoiceInput");
        Directory.CreateDirectory(settingsDir);
        _settingsPath = Path.Combine(settingsDir, "settings.json");
        _current = Load();
    }

    public AppSettings Current => _current;

    public AppSettings Load()
    {
        lock (_lock)
        {
            if (!File.Exists(_settingsPath))
            {
                return new AppSettings();
            }

            try
            {
                var json = File.ReadAllText(_settingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
            catch
            {
                return new AppSettings();
            }
        }
    }

    public void Save(AppSettings settings)
    {
        lock (_lock)
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(settings, options);

            var tempPath = _settingsPath + ".tmp";
            var backupPath = _settingsPath + ".bak";

            try
            {
                File.WriteAllText(tempPath, json);

                if (File.Exists(_settingsPath))
                {
                    File.Replace(tempPath, _settingsPath, backupPath);
                }
                else
                {
                    File.Move(tempPath, _settingsPath);
                }

                _current = settings;
            }
            catch
            {
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch { }
                }
                throw;
            }
        }
    }
}
