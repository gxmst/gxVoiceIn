using System.Drawing;
using System.Windows.Forms;
using VoiceInputApp.Models;
using VoiceInputApp.Services.Settings;
using VoiceInputApp.Services.Startup;

namespace VoiceInputApp.Services.Tray;

public class TrayIconService : ITrayIconService, IDisposable
{
    private readonly ISettingsService _settingsService;
    private readonly IAutoStartService _autoStartService;
    private NotifyIcon? _notifyIcon;
    private readonly Action? _onQuit;
    private readonly Action? _onOpenDashboard;
    private readonly Action? _onAsrSettings;
    private readonly Action? _onLlmSettings;
    private readonly Action<Language>? _onLanguageChanged;
    private readonly Action<bool>? _onLlmEnabledChanged;
    private readonly Action<bool>? _onAutoStartChanged;
    private readonly Action<InteractionMode>? _onSetMode;
    private readonly Action? _onStopPlayback;
    private readonly Action? _onClearConversation;

    public TrayIconService(
        ISettingsService settingsService,
        IAutoStartService autoStartService,
        Action? onQuit = null,
        Action? onOpenDashboard = null,
        Action? onAsrSettings = null,
        Action? onLlmSettings = null,
        Action<Language>? onLanguageChanged = null,
        Action<bool>? onLlmEnabledChanged = null,
        Action<bool>? onAutoStartChanged = null,
        Action<InteractionMode>? onSetMode = null,
        Action? onStopPlayback = null,
        Action? onClearConversation = null)
    {
        _settingsService = settingsService;
        _autoStartService = autoStartService;
        _onQuit = onQuit;
        _onOpenDashboard = onOpenDashboard;
        _onAsrSettings = onAsrSettings;
        _onLlmSettings = onLlmSettings;
        _onLanguageChanged = onLanguageChanged;
        _onLlmEnabledChanged = onLlmEnabledChanged;
        _onAutoStartChanged = onAutoStartChanged;
        _onSetMode = onSetMode;
        _onStopPlayback = onStopPlayback;
        _onClearConversation = onClearConversation;
    }

    public void Initialize()
    {
        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "语音输入法",
            Visible = true
        };
        _notifyIcon.DoubleClick += (s, e) => _onOpenDashboard?.Invoke();

        UpdateMenu();
    }

    public void Show()
    {
        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = true;
        }
    }

    public void Hide()
    {
        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
        }
    }

    public NotifyIcon GetNotifyIcon()
    {
        return _notifyIcon!;
    }

    public void UpdateMenu()
    {
        if (_notifyIcon == null) return;

        var settings = _settingsService.Current;
        var menu = new ContextMenuStrip();

        var modeItem = new ToolStripMenuItem("交互模式");
        foreach (InteractionMode mode in Enum.GetValues(typeof(InteractionMode)))
        {
            var modeName = mode switch
            {
                InteractionMode.Input => "输入模式",
                InteractionMode.Conversation => "对话模式",
                InteractionMode.Hybrid => "混合模式",
                _ => mode.ToString()
            };
            var item = new ToolStripMenuItem(modeName)
            {
                Checked = settings.Mode == mode,
                Tag = mode
            };
            item.Click += (s, e) =>
            {
                if (s is ToolStripMenuItem menuItem && menuItem.Tag is InteractionMode selectedMode)
                {
                    _onSetMode?.Invoke(selectedMode);
                }
            };
            modeItem.DropDownItems.Add(item);
        }
        menu.Items.Add(modeItem);

        menu.Items.Add(new ToolStripSeparator());

        var languageItem = new ToolStripMenuItem("语言");
        foreach (Language lang in Enum.GetValues(typeof(Language)))
        {
            var item = new ToolStripMenuItem(lang.ToDisplayName())
            {
                Checked = settings.Language == lang,
                Tag = lang
            };
            item.Click += (s, e) =>
            {
                if (s is ToolStripMenuItem menuItem && menuItem.Tag is Language selectedLang)
                {
                    _settingsService.Current.Language = selectedLang;
                    _settingsService.Save(_settingsService.Current);
                    _onLanguageChanged?.Invoke(selectedLang);
                    UpdateMenu();
                }
            };
            languageItem.DropDownItems.Add(item);
        }
        menu.Items.Add(languageItem);

        menu.Items.Add(new ToolStripSeparator());

        var dashboardItem = new ToolStripMenuItem("控制台");
        dashboardItem.Click += (s, e) => _onOpenDashboard?.Invoke();
        menu.Items.Add(dashboardItem);

        var stopPlaybackItem = new ToolStripMenuItem("停止播放");
        stopPlaybackItem.Click += (s, e) => _onStopPlayback?.Invoke();
        menu.Items.Add(stopPlaybackItem);

        var clearConversationItem = new ToolStripMenuItem("清空会话");
        clearConversationItem.Click += (s, e) => _onClearConversation?.Invoke();
        menu.Items.Add(clearConversationItem);

        menu.Items.Add(new ToolStripSeparator());

        var autoStartItem = new ToolStripMenuItem("开机自启")
        {
            Checked = _autoStartService.IsEnabled()
        };
        autoStartItem.Click += (s, e) =>
        {
            var newEnabled = !_autoStartService.IsEnabled();
            _autoStartService.SetEnabled(newEnabled);
            _onAutoStartChanged?.Invoke(newEnabled);
            UpdateMenu();
        };
        menu.Items.Add(autoStartItem);

        var llmItem = new ToolStripMenuItem("LLM Refinement");
        var enableItem = new ToolStripMenuItem("Enabled")
        {
            Checked = settings.LlmEnabled
        };
        enableItem.Click += (s, e) =>
        {
            var newEnabled = !settings.LlmEnabled;
            _settingsService.Current.LlmEnabled = newEnabled;
            _settingsService.Save(_settingsService.Current);
            _onLlmEnabledChanged?.Invoke(newEnabled);
            UpdateMenu();
        };
        llmItem.DropDownItems.Add(enableItem);

        var llmSettingsItem = new ToolStripMenuItem("Settings...");
        llmSettingsItem.Click += (s, e) => _onLlmSettings?.Invoke();
        llmItem.DropDownItems.Add(llmSettingsItem);
        menu.Items.Add(llmItem);

        var asrSettingsItem = new ToolStripMenuItem("ASR Settings...");
        asrSettingsItem.Click += (s, e) => _onAsrSettings?.Invoke();
        menu.Items.Add(asrSettingsItem);

        menu.Items.Add(new ToolStripSeparator());

        var quitItem = new ToolStripMenuItem("Quit");
        quitItem.Click += (s, e) => _onQuit?.Invoke();
        menu.Items.Add(quitItem);

        _notifyIcon.ContextMenuStrip = menu;
    }

    public void Dispose()
    {
        _notifyIcon?.Dispose();
    }
}
