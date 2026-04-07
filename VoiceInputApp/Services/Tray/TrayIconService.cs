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

    public TrayIconService(
        ISettingsService settingsService,
        IAutoStartService autoStartService,
        Action? onQuit = null,
        Action? onOpenDashboard = null,
        Action? onAsrSettings = null,
        Action? onLlmSettings = null,
        Action<Language>? onLanguageChanged = null,
        Action<bool>? onLlmEnabledChanged = null,
        Action<bool>? onAutoStartChanged = null)
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
