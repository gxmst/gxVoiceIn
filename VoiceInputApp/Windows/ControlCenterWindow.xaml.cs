using System.Diagnostics;
using System.IO;
using System.Windows;
using VoiceInputApp.Models;
using VoiceInputApp.Services.Logging;
using VoiceInputApp.Services.Settings;
using VoiceInputApp.Services.Startup;

namespace VoiceInputApp.Windows;

public partial class ControlCenterWindow : Window
{
    private readonly ISettingsService _settingsService;
    private readonly ILoggingService _loggingService;
    private readonly IAutoStartService _autoStartService;
    private readonly Action _openAsrSettings;
    private readonly Action _openLlmSettings;
    private readonly Action _quitApplication;

    public ControlCenterWindow(
        ISettingsService settingsService,
        ILoggingService loggingService,
        IAutoStartService autoStartService,
        Action openAsrSettings,
        Action openLlmSettings,
        Action quitApplication)
    {
        InitializeComponent();
        _settingsService = settingsService;
        _loggingService = loggingService;
        _autoStartService = autoStartService;
        _openAsrSettings = openAsrSettings;
        _openLlmSettings = openLlmSettings;
        _quitApplication = quitApplication;

        Activated += (s, e) => RefreshContent();
        RefreshContent();
    }

    public void RefreshContent()
    {
        var settings = _settingsService.Current;

        AsrStatusText.Text = string.IsNullOrWhiteSpace(settings.Asr.AppId) || string.IsNullOrWhiteSpace(settings.Asr.Token)
            ? "未配置"
            : "已就绪";
        AsrStatusText.Foreground = AsrStatusText.Text == "已就绪"
            ? System.Windows.Media.Brushes.SeaGreen
            : System.Windows.Media.Brushes.IndianRed;

        LanguageText.Text = $"当前语言：{settings.Language.ToDisplayName()}";

        LlmStatusText.Text = settings.LlmEnabled ? "已开启" : "已关闭";
        LlmStatusText.Foreground = settings.LlmEnabled
            ? System.Windows.Media.Brushes.DarkCyan
            : System.Windows.Media.Brushes.DimGray;

        LlmModelText.Text = string.IsNullOrWhiteSpace(settings.Llm.Model)
            ? "当前未配置模型"
            : $"当前模型：{settings.Llm.Model}";

        var autoStartEnabled = _autoStartService.IsEnabled();
        AutoStartStatusText.Text = autoStartEnabled ? "已开启" : "已关闭";
        AutoStartStatusText.Foreground = autoStartEnabled
            ? System.Windows.Media.Brushes.MediumPurple
            : System.Windows.Media.Brushes.DimGray;
        AutoStartButton.Content = autoStartEnabled ? "关闭开机自启" : "开启开机自启";
        AutoStartButton.Background = autoStartEnabled
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(237, 233, 254))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(224, 242, 254));

        LogPathText.Text = _loggingService.GetLogFilePath();
        RecentLogsBox.Text = _loggingService.GetRecentLogs(120);
        RecentLogsBox.ScrollToHome();
        UpdatedAtText.Text = $"更新于 {DateTime.Now:HH:mm:ss}";
    }

    private void OpenAsrSettings_Click(object sender, RoutedEventArgs e)
    {
        _openAsrSettings();
        RefreshContent();
    }

    private void OpenLlmSettings_Click(object sender, RoutedEventArgs e)
    {
        _openLlmSettings();
        RefreshContent();
    }

    private void OpenLogFolder_Click(object sender, RoutedEventArgs e)
    {
        var logFilePath = _loggingService.GetLogFilePath();
        var logDirectory = Path.GetDirectoryName(logFilePath);
        if (!string.IsNullOrWhiteSpace(logDirectory))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = logDirectory,
                UseShellExecute = true
            });
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        RefreshContent();
    }

    private void ToggleAutoStart_Click(object sender, RoutedEventArgs e)
    {
        _autoStartService.SetEnabled(!_autoStartService.IsEnabled());
        RefreshContent();
    }

    private void Quit_Click(object sender, RoutedEventArgs e)
    {
        Close();
        _quitApplication();
    }
}
