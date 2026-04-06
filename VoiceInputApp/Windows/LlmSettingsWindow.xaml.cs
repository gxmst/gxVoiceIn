using System.Windows;
using VoiceInputApp.Services.Settings;
using VoiceInputApp.ViewModels;

namespace VoiceInputApp.Windows;

public partial class LlmSettingsWindow : Window
{
    private readonly LlmSettingsViewModel _viewModel;

    public LlmSettingsWindow(ISettingsService settingsService)
    {
        InitializeComponent();
        _viewModel = new LlmSettingsViewModel(settingsService);
        _viewModel.CloseRequested += () => Close();
        DataContext = _viewModel;

        ApiKeyBox.Password = _viewModel.ApiKey;
    }

    private void ApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        _viewModel.ApiKey = ApiKeyBox.Password;
    }
}
