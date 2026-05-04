using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using VoiceInputApp.Models;
using VoiceInputApp.Services;
using VoiceInputApp.Services.Conversation;
using VoiceInputApp.Services.Logging;
using VoiceInputApp.Services.Settings;
using VoiceInputApp.Services.Startup;

namespace VoiceInputApp.Windows;

public partial class ControlCenterWindow : Window
{
    private readonly ISettingsService _settingsService;
    private readonly ILoggingService _loggingService;
    private readonly IAutoStartService _autoStartService;
    private readonly IConversationSessionStore _conversationSessionStore;
    private readonly VoiceInputOrchestrator _orchestrator;
    private readonly Action _openAsrSettings;
    private readonly Action _openLlmSettings;
    private readonly Action _quitApplication;

    public ControlCenterWindow(
        ISettingsService settingsService,
        ILoggingService loggingService,
        IAutoStartService autoStartService,
        IConversationSessionStore conversationSessionStore,
        VoiceInputOrchestrator orchestrator,
        Action openAsrSettings,
        Action openLlmSettings,
        Action quitApplication)
    {
        InitializeComponent();
        _settingsService = settingsService;
        _loggingService = loggingService;
        _autoStartService = autoStartService;
        _conversationSessionStore = conversationSessionStore;
        _orchestrator = orchestrator;
        _openAsrSettings = openAsrSettings;
        _openLlmSettings = openLlmSettings;
        _quitApplication = quitApplication;

        ConversationItemsControl.ItemTemplateSelector = new ConversationTemplateSelector();

        Activated += (_, _) => RefreshContent();
        _orchestrator.SnapshotChanged += OnSnapshotChanged;
        Closed += (_, _) => _orchestrator.SnapshotChanged -= OnSnapshotChanged;
        RefreshContent();
    }

    public void RefreshContent()
    {
        var settings = _settingsService.Current;
        var snapshot = _orchestrator.GetSnapshot();

        ModeText.Text = settings.Mode switch
        {
            InteractionMode.Input => "输入模式",
            InteractionMode.Conversation => "对话模式",
            _ => "混合模式"
        };

        CurrentStateText.Text = snapshot.StateText;
        CurrentStateText.Foreground = snapshot.IsPlaying
            ? System.Windows.Media.Brushes.MediumSlateBlue
            : System.Windows.Media.Brushes.DarkSlateGray;
        PlaybackStateText.Text = snapshot.IsPlaying ? "当前正在播放语音回复" : "当前没有播放任务";

        HotkeyInfoText.Text = $"热键：{snapshot.TriggerKeyDisplay}";
        LanguageInfoText.Text = $"语言：{snapshot.LanguageDisplay}　TTS 音色：{snapshot.TtsVoiceDisplay}";

        var asrReady = !string.IsNullOrWhiteSpace(settings.Asr.AppId) && !string.IsNullOrWhiteSpace(settings.Asr.Token);
        AsrStatusText.Text = asrReady ? "ASR 已就绪" : "ASR 未配置";
        AsrStatusText.Foreground = asrReady
            ? System.Windows.Media.Brushes.SeaGreen
            : System.Windows.Media.Brushes.IndianRed;

        var conversationModel = string.IsNullOrWhiteSpace(settings.Llm.ConversationModel)
            ? settings.Llm.Model
            : settings.Llm.ConversationModel;
        LlmModelText.Text = string.IsNullOrWhiteSpace(conversationModel)
            ? "当前未配置对话模型"
            : $"对话模型：{conversationModel}\nTTS：{settings.Llm.TtsModel} / {settings.Llm.TtsVoice}\n原生音频：{(settings.Llm.UseModelNativeAudio ? "已启用" : "未启用")}";

        RecognizedTextBox.Text = string.IsNullOrWhiteSpace(snapshot.LastRecognizedText)
            ? "暂无识别结果"
            : snapshot.LastRecognizedText;
        AssistantReplyBox.Text = string.IsNullOrWhiteSpace(snapshot.LastAssistantReply)
            ? "暂无助手回复"
            : snapshot.LastAssistantReply;

        BuildConversationHistory();

        LogPathText.Text = _loggingService.GetLogFilePath();
        RecentLogsBox.Text = _loggingService.GetRecentLogs(120);
        RecentLogsBox.ScrollToHome();
        UpdatedAtText.Text = $"更新于 {DateTime.Now:HH:mm:ss}";
    }

    private void BuildConversationHistory()
    {
        var messages = _conversationSessionStore.GetMessages();
        ConversationTurnCountText.Text = messages.Count > 0 ? $"共 {messages.Count / 2} 轮对话" : string.Empty;

        if (messages.Count == 0)
        {
            ConversationItemsControl.ItemsSource = null;
            return;
        }

        var items = messages.Select(m => new ConversationItemViewModel
        {
            Role = m.Role,
            Content = m.Content,
            TimeDisplay = m.Timestamp.ToString("HH:mm:ss")
        }).ToList();

        ConversationItemsControl.ItemsSource = items;
    }

    private void OnSnapshotChanged(object? sender, VoiceInteractionSnapshot e)
    {
        Dispatcher.InvokeAsync(RefreshContent);
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

    private void SetInputMode_Click(object sender, RoutedEventArgs e)
    {
        _orchestrator.SetMode(InteractionMode.Input);
        RefreshContent();
    }

    private void SetConversationMode_Click(object sender, RoutedEventArgs e)
    {
        _orchestrator.SetMode(InteractionMode.Conversation);
        RefreshContent();
    }

    private void SetHybridMode_Click(object sender, RoutedEventArgs e)
    {
        _orchestrator.SetMode(InteractionMode.Hybrid);
        RefreshContent();
    }

    private void StopPlayback_Click(object sender, RoutedEventArgs e)
    {
        _orchestrator.StopPlayback();
        RefreshContent();
    }

    private void ClearConversation_Click(object sender, RoutedEventArgs e)
    {
        _orchestrator.ClearConversationHistory();
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

    private void Quit_Click(object sender, RoutedEventArgs e)
    {
        Close();
        _quitApplication();
    }
}

public class ConversationItemViewModel
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string TimeDisplay { get; set; } = string.Empty;
}

public class ConversationTemplateSelector : DataTemplateSelector
{
    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        if (item is not ConversationItemViewModel vm) return null;
        if (container is not FrameworkElement fe) return null;

        return vm.Role switch
        {
            "user" => fe.FindResource("UserMessageTemplate") as DataTemplate,
            "assistant" => fe.FindResource("AssistantMessageTemplate") as DataTemplate,
            _ => null
        };
    }
}
