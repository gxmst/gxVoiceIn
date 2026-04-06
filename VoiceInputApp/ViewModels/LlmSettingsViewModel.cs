using System.ComponentModel;
using System.Net.Http;
using System.Runtime.CompilerServices;
using VoiceInputApp.Services.Settings;

namespace VoiceInputApp.ViewModels;

public class LlmSettingsViewModel : INotifyPropertyChanged
{
    private readonly ISettingsService _settingsService;
    private string _baseUrl = string.Empty;
    private string _apiKey = string.Empty;
    private string _model = string.Empty;
    private string _statusMessage = string.Empty;
    private bool _isTesting;

    public string BaseUrl
    {
        get => _baseUrl;
        set
        {
            if (_baseUrl != value)
            {
                _baseUrl = value;
                OnPropertyChanged();
            }
        }
    }

    public string ApiKey
    {
        get => _apiKey;
        set
        {
            if (_apiKey != value)
            {
                _apiKey = value;
                OnPropertyChanged();
            }
        }
    }

    public string Model
    {
        get => _model;
        set
        {
            if (_model != value)
            {
                _model = value;
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

    public RelayCommand SaveCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand TestCommand { get; }

    public event Action? CloseRequested;
    public event PropertyChangedEventHandler? PropertyChanged;

    public LlmSettingsViewModel(ISettingsService settingsService)
    {
        _settingsService = settingsService;

        var settings = settingsService.Current.Llm;
        _baseUrl = settings.BaseUrl;
        _apiKey = settings.ApiKey;
        _model = settings.Model;

        SaveCommand = new RelayCommand(Save);
        CancelCommand = new RelayCommand(() => CloseRequested?.Invoke());
        TestCommand = new RelayCommand(async () => await TestAsync());
    }

    private void Save()
    {
        _settingsService.Current.Llm.BaseUrl = _baseUrl;
        _settingsService.Current.Llm.ApiKey = _apiKey;
        _settingsService.Current.Llm.Model = _model;
        _settingsService.Save(_settingsService.Current);
        CloseRequested?.Invoke();
    }

    private async Task TestAsync()
    {
        if (string.IsNullOrEmpty(_baseUrl) || string.IsNullOrEmpty(_apiKey) || string.IsNullOrEmpty(_model))
        {
            StatusMessage = "请填写所有配置项";
            return;
        }

        IsTesting = true;
        StatusMessage = "测试中...";

        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");

            var url = _baseUrl.TrimEnd('/') + "/models";
            var response = await client.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                StatusMessage = "配置有效";
            }
            else
            {
                StatusMessage = $"测试失败: HTTP {(int)response.StatusCode}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"测试失败: {ex.Message}";
        }
        finally
        {
            IsTesting = false;
        }
    }

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
