using System.Windows;
using VoiceInputApp.Services.Settings;
using VoiceInputApp.ViewModels;

namespace VoiceInputApp.Windows;

public partial class AsrSettingsWindow : Window
{
    private readonly AsrSettingsViewModel _viewModel;

    public AsrSettingsWindow(ISettingsService settingsService)
    {
        InitializeComponent();
        _viewModel = new AsrSettingsViewModel(settingsService);
        _viewModel.CloseRequested += () => Close();
        DataContext = _viewModel;

        TokenBox.Password = _viewModel.Token;
    }

    private void TokenBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        _viewModel.Token = TokenBox.Password;
    }
}
