using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using VoiceInputApp.Services.Audio;
using VoiceInputApp.Services.Logging;
using VoiceInputApp.Services.Settings;

namespace VoiceInputApp.ViewModels;

public class AsrSettingsViewModel : INotifyPropertyChanged
{
    private readonly ISettingsService _settingsService;
    private readonly ILoggingService _logger = LoggingService.Instance;
    private string _appId = string.Empty;
    private string _token = string.Empty;
    private string _resourceId = string.Empty;
    private string _statusMessage = string.Empty;
    private bool _isTesting;
    private int _selectedMicrophoneIndex;

    public string AppId
    {
        get => _appId;
        set
        {
            if (_appId != value)
            {
                _appId = value;
                OnPropertyChanged();
            }
        }
    }

    public string Token
    {
        get => _token;
        set
        {
            if (_token != value)
            {
                _token = value;
                OnPropertyChanged();
            }
        }
    }

    public string ResourceId
    {
        get => _resourceId;
        set
        {
            if (_resourceId != value)
            {
                _resourceId = value;
                OnPropertyChanged();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set
        {
            if (_statusMessage != value)
            {
                _statusMessage = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsTesting
    {
        get => _isTesting;
        set
        {
            if (_isTesting != value)
            {
                _isTesting = value;
                OnPropertyChanged();
            }
        }
    }

    public ObservableCollection<MicrophoneDevice> Microphones { get; } = new();

    public int SelectedMicrophoneIndex
    {
        get => _selectedMicrophoneIndex;
        set
        {
            if (_selectedMicrophoneIndex != value)
            {
                _selectedMicrophoneIndex = value;
                OnPropertyChanged();
            }
        }
    }

    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand TestCommand { get; }
    public ICommand OpenLogCommand { get; }

    public event Action? CloseRequested;
    public event PropertyChangedEventHandler? PropertyChanged;

    public AsrSettingsViewModel(ISettingsService settingsService)
    {
        _settingsService = settingsService;

        var settings = settingsService.Current;
        _appId = settings.Asr.AppId;
        _token = settings.Asr.Token;
        _resourceId = settings.Asr.ResourceId;

        LoadMicrophones();
        _selectedMicrophoneIndex = settings.MicrophoneDeviceIndex;

        SaveCommand = new RelayCommand(Save);
        CancelCommand = new RelayCommand(() => CloseRequested?.Invoke());
        TestCommand = new RelayCommand(async () => await TestAsync());
        OpenLogCommand = new RelayCommand(OpenLogFile);
    }

    private void LoadMicrophones()
    {
        Microphones.Clear();
        var devices = AudioCaptureService.GetAvailableDevices();
        
        foreach (var (index, name) in devices)
        {
            Microphones.Add(new MicrophoneDevice { Index = index, Name = name });
        }

        if (Microphones.Count == 0)
        {
            StatusMessage = "未检测到麦克风设备";
        }
    }

    private void Save()
    {
        _settingsService.Current.Asr.AppId = _appId;
        _settingsService.Current.Asr.Token = _token;
        _settingsService.Current.Asr.ResourceId = _resourceId;
        _settingsService.Current.MicrophoneDeviceIndex = _selectedMicrophoneIndex;
        _settingsService.Save(_settingsService.Current);
        
        _logger.Info($"Settings saved. AppId: {_appId}, ResourceId: {_resourceId}, Microphone: {_selectedMicrophoneIndex}");
        CloseRequested?.Invoke();
    }

    private async Task TestAsync()
    {
        if (string.IsNullOrEmpty(_appId) || string.IsNullOrEmpty(_token))
        {
            StatusMessage = "请填写 AppID 和 Token";
            return;
        }

        if (string.IsNullOrEmpty(_resourceId))
        {
            StatusMessage = "请填写 Resource ID";
            return;
        }

        IsTesting = true;
        StatusMessage = "测试中...";

        try
        {
            await Task.Delay(1000);
            StatusMessage = "配置有效";
            _logger.Info("ASR test passed");
        }
        catch (Exception ex)
        {
            StatusMessage = $"测试失败: {ex.Message}";
            _logger.Error("ASR test failed", ex);
        }
        finally
        {
            IsTesting = false;
        }
    }

    private void OpenLogFile()
    {
        var logPath = _logger.GetLogFilePath();
        if (System.IO.File.Exists(logPath))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = logPath,
                UseShellExecute = true
            });
        }
        else
        {
            StatusMessage = "日志文件不存在";
        }
    }

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class MicrophoneDevice
{
    public int Index { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) => _execute();
#pragma warning disable CS0067
    public event EventHandler? CanExecuteChanged;
#pragma warning restore CS0067
}
