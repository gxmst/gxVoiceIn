using System.Windows;
using VoiceInputApp.Models;
using VoiceInputApp.Services;
using VoiceInputApp.Services.Audio;
using VoiceInputApp.Services.Hotkey;
using VoiceInputApp.Services.Injection;
using VoiceInputApp.Services.LLM;
using VoiceInputApp.Services.Logging;
using VoiceInputApp.Services.Notification;
using VoiceInputApp.Services.Settings;
using VoiceInputApp.Services.Startup;
using VoiceInputApp.Services.Tray;
using VoiceInputApp.Services.Transcription;
using VoiceInputApp.ViewModels;
using VoiceInputApp.Windows;

namespace VoiceInputApp;

public partial class App : Application
{
    private ILoggingService _logger = LoggingService.Instance;
    private ISettingsService? _settingsService;
    private IHotkeyMonitor? _hotkeyMonitor;
    private AudioCaptureService? _audioCaptureService;
    private ITranscriptionService? _transcriptionService;
    private ITextInjectionService? _textInjectionService;
    private ILlmRefinementService? _llmRefinementService;
    private INotificationService? _notificationService;
    private TrayIconService? _trayIconService;
    private VoiceInputOrchestrator? _orchestrator;
    private HudManager? _hudManager;
    private ControlCenterWindow? _controlCenterWindow;
    private IAutoStartService? _autoStartService;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        InitializeServices();
        StartApplication();
    }

    private void InitializeServices()
    {
        _logger.Info("Initializing services");
        
        _settingsService = new SettingsService();
        _autoStartService = new AutoStartService();
        _hotkeyMonitor = new HotkeyMonitor();
        _audioCaptureService = new AudioCaptureService();
        _transcriptionService = new VolcengineAsrService(_settingsService);
        _textInjectionService = new ClipboardInjectionService();
        _llmRefinementService = new OpenAiRefinementService(_settingsService);
        _hudManager = new HudManager();

        _trayIconService = new TrayIconService(
            _settingsService,
            _autoStartService,
            onQuit: QuitApplication,
            onOpenDashboard: ShowControlCenter,
            onAsrSettings: ShowAsrSettings,
            onLlmSettings: ShowLlmSettings,
            onLanguageChanged: OnLanguageChanged,
            onLlmEnabledChanged: OnLlmEnabledChanged,
            onAutoStartChanged: OnAutoStartChanged);

        _trayIconService.Initialize();
        _notificationService = new TrayNotificationService(_trayIconService.GetNotifyIcon());

        _orchestrator = new VoiceInputOrchestrator(
            _hotkeyMonitor,
            _audioCaptureService,
            _transcriptionService,
            _textInjectionService,
            _llmRefinementService,
            _settingsService,
            _notificationService,
            new LoggerWrapper(_logger),
            _hudManager);
    }

    private void StartApplication()
    {
        _logger.Info("Application starting");
        _orchestrator?.Start();

        if (!_transcriptionService!.IsConfigured)
        {
            _notificationService?.Show("语音输入", "请先配置火山引擎 ASR 设置", NotificationType.Warning);
        }
    }

    private void ShowAsrSettings()
    {
        var window = new AsrSettingsWindow(_settingsService!);
        window.ShowDialog();
        _trayIconService?.UpdateMenu();
        _controlCenterWindow?.RefreshContent();
    }

    private void ShowLlmSettings()
    {
        var window = new LlmSettingsWindow(_settingsService!);
        window.ShowDialog();
        _trayIconService?.UpdateMenu();
        _controlCenterWindow?.RefreshContent();
    }

    private void ShowControlCenter()
    {
        if (_controlCenterWindow == null || !_controlCenterWindow.IsLoaded)
        {
            _controlCenterWindow = new ControlCenterWindow(
                _settingsService!,
                _logger,
                _autoStartService!,
                ShowAsrSettings,
                ShowLlmSettings,
                QuitApplication);
            _controlCenterWindow.Closed += (s, e) => _controlCenterWindow = null;
            _controlCenterWindow.Show();
            return;
        }

        _controlCenterWindow.RefreshContent();
        if (_controlCenterWindow.WindowState == WindowState.Minimized)
        {
            _controlCenterWindow.WindowState = WindowState.Normal;
        }

        _controlCenterWindow.Activate();
    }

    private void OnLanguageChanged(Language language)
    {
        _logger.Info($"Language changed to {language}");
    }

    private void OnLlmEnabledChanged(bool enabled)
    {
        _logger.Info($"LLM enabled: {enabled}");
        _controlCenterWindow?.RefreshContent();
    }

    private void OnAutoStartChanged(bool enabled)
    {
        _logger.Info($"Auto start enabled: {enabled}");
        _controlCenterWindow?.RefreshContent();
    }

    private void QuitApplication()
    {
        _logger.Info("Application quitting");
        _orchestrator?.Dispose();
        _hudManager?.CloseAll();
        _controlCenterWindow?.Close();
        _trayIconService?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _orchestrator?.Dispose();
        _hudManager?.CloseAll();
        _controlCenterWindow?.Close();
        _trayIconService?.Dispose();
        base.OnExit(e);
    }
}

internal class LoggerWrapper : ILogger
{
    private readonly ILoggingService _loggingService;

    public LoggerWrapper(ILoggingService loggingService)
    {
        _loggingService = loggingService;
    }

    public void Debug(string message) => _loggingService.Debug(message);
    public void Info(string message) => _loggingService.Info(message);
    public void Warning(string message) => _loggingService.Warning(message);
    public void Error(string message, Exception? exception = null) => _loggingService.Error(message, exception);
}
