using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Media;
using System.Windows.Interop;
using VoiceInputApp.ViewModels;
using VoiceInputApp.Models;

namespace VoiceInputApp.Windows;

public partial class HudWindow : Window
{
    private readonly HudViewModel _viewModel;
    private bool _hasShown;
    private double _lastWidth;
    private readonly System.Windows.Threading.DispatcherTimer _widthTimer;

    public HudWindow(HudViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;
        ShowActivated = false;
        Focusable = false;

        _widthTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(180)
        };
        _widthTimer.Tick += (s, e) => { _widthTimer.Stop(); CommitWidthAnimation(); };

        Loaded += OnLoaded;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HudViewModel.DisplayText))
        {
            Dispatcher.InvokeAsync(PlayTextUpdateAnimation);
            RequestWidthUpdate();
        }
        else if (e.PropertyName == nameof(HudViewModel.State))
        {
            if (_viewModel.State == HudState.Success || _viewModel.State == HudState.Error)
            {
                // Instant final adjustment
                _widthTimer.Stop();
                CommitWidthAnimation(isFinal: true);
            }
        }
    }

    private void RequestWidthUpdate()
    {
        if (_viewModel.State != HudState.Listening)
        {
            CommitWidthAnimation();
            return;
        }

        if (!_widthTimer.IsEnabled)
        {
            _widthTimer.Start();
        }
    }

    private void CommitWidthAnimation(bool isFinal = false)
    {
        // Measure target width
        HudShell.BeginAnimation(WidthProperty, null);
        HudShell.Width = double.NaN;
        HudShell.UpdateLayout();
        
        double newTarget = HudShell.ActualWidth;
        
        // During listening, only allow expansion or very slow contraction to stop flickering
        if (!isFinal && _viewModel.State == HudState.Listening && newTarget < _lastWidth)
        {
            newTarget = _lastWidth; // Keep it stable
        }

        if (Math.Abs(newTarget - _lastWidth) < 1.0 && !isFinal) return;

        var anim = new DoubleAnimation(_lastWidth > 0 ? _lastWidth : newTarget, newTarget, TimeSpan.FromMilliseconds(isFinal ? 300 : 200))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        
        HudShell.BeginAnimation(WidthProperty, anim);
        _lastWidth = newTarget;
    }

    private void PlayTextUpdateAnimation()
    {
        var fade = new DoubleAnimation(0.7, 1.0, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        DisplayTextBlock.BeginAnimation(OpacityProperty, fade);

        var nudge = new DoubleAnimation(2, 0, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        var transform = new TranslateTransform();
        DisplayTextBlock.RenderTransform = transform;
        transform.BeginAnimation(TranslateTransform.XProperty, nudge);
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

        // Removed Windows 11 Acrylic/Mica Backdrop due to Window visual artifacts
        // EnableBlur(hwnd);

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
