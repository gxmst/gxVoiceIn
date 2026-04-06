using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Interop;
using VoiceInputApp.ViewModels;

namespace VoiceInputApp.Windows;

public partial class HudWindow : Window
{
    private readonly HudViewModel _viewModel;
    private bool _hasShown;

    public HudWindow(HudViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SetWindowStyle();
    }

    private void SetWindowStyle()
    {
        var hwnd = new WindowInteropHelper(this).Handle;

        const int GWL_EXSTYLE = -20;
        const int WS_EX_NOACTIVATE = 0x08000000;
        const int WS_EX_TOOLWINDOW = 0x00000080;

        var extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        extendedStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
        SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle);

        ShowInTaskbar = false;
        Topmost = true;
    }

    public void ShowHud()
    {
        if (!IsVisible)
        {
            Show();
        }

        if (!_hasShown)
        {
            PlayEntranceAnimation();
            _hasShown = true;
        }
    }

    public void AnimateStateChange()
    {
        var dotPulse = new DoubleAnimation(0.85, 1.15, TimeSpan.FromMilliseconds(240))
        {
            AutoReverse = true,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        StateDot.BeginAnimation(WidthProperty, dotPulse);
        StateDot.BeginAnimation(HeightProperty, dotPulse);

        var shellNudge = new DoubleAnimation(0, -2, TimeSpan.FromMilliseconds(120))
        {
            AutoReverse = true,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        HudTranslateTransform.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, shellNudge);
    }

    private void PlayEntranceAnimation()
    {
        Opacity = 0;

        var duration = TimeSpan.FromMilliseconds(240);
        var ease = new BackEase { Amplitude = 0.22, EasingMode = EasingMode.EaseOut };

        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        HudScaleTransform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, new DoubleAnimation(0.985, 1, duration) { EasingFunction = ease });
        HudScaleTransform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, new DoubleAnimation(0.985, 1, duration) { EasingFunction = ease });
        HudTranslateTransform.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, new DoubleAnimation(22, 0, duration) { EasingFunction = ease });
    }

    public void HideHud()
    {
        Hide();
    }

    public void SetPosition(double left, double top)
    {
        Left = left;
        Top = top;
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);
}
