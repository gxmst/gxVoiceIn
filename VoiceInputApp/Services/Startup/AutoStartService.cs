using System.IO;
using Microsoft.Win32;

namespace VoiceInputApp.Services.Startup;

public class AutoStartService : IAutoStartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "gxVoiceIn";

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
        var value = key?.GetValue(AppName) as string;
        return !string.IsNullOrWhiteSpace(value);
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true)
            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);

        if (enabled)
        {
            key.SetValue(AppName, GetLaunchCommand());
        }
        else
        {
            key.DeleteValue(AppName, false);
        }
    }

    private static string GetLaunchCommand()
    {
        var baseDirectoryExePath = Path.Combine(AppContext.BaseDirectory, "VoiceInput.exe");
        if (File.Exists(baseDirectoryExePath))
        {
            return $"\"{baseDirectoryExePath}\"";
        }

        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath) && File.Exists(processPath))
        {
            return $"\"{processPath}\"";
        }

        return $"\"{baseDirectoryExePath}\"";
    }
}
