using System.Windows;
using System.Windows.Media.Animation;
using VoiceInputApp.Models;
using VoiceInputApp.Services.Logging;
using VoiceInputApp.ViewModels;
using VoiceInputApp.Windows;

namespace VoiceInputApp.Services;

public class HudManager
{
    private readonly ILoggingService _logger = LoggingService.Instance;
    private readonly List<HudInstance> _activeHuds = new();
    private readonly object _lock = new();

    public HudInstance CreateHud()
    {
        var viewModel = new HudViewModel();
        var window = new HudWindow(viewModel);

        var instance = new HudInstance(viewModel, window, this);

        lock (_lock)
        {
            _activeHuds.Add(instance);
            _logger.Info($"Created HUD, total active: {_activeHuds.Count}");
        }

        window.Closed += (s, e) =>
        {
            lock (_lock)
            {
                _activeHuds.Remove(instance);
                _logger.Info($"HUD closed, total active: {_activeHuds.Count}");
            }
        };

        return instance;
    }

    public void UpdatePositions()
    {
        List<HudInstance> huds;
        lock (_lock)
        {
            huds = _activeHuds.ToList();
        }

        var screenWidth = SystemParameters.PrimaryScreenWidth;
        var screenHeight = SystemParameters.PrimaryScreenHeight;
        var baseBottom = screenHeight - 80;

        for (int i = 0; i < huds.Count; i++)
        {
            var hud = huds[i];
            var offset = i * 70;
            try
            {
                hud.Window.Dispatcher.BeginInvoke(() =>
                {
                    hud.Window.UpdateLayout();
                    var left = (screenWidth - hud.Window.ActualWidth) / 2;
                    var top = baseBottom - offset - hud.Window.ActualHeight;
                    hud.Window.SetPosition(left, top);
                });
            }
            catch (Exception ex)
            {
                _logger.Warning($"Failed to update HUD position: {ex.Message}");
            }
        }
    }

    public void CloseAll()
    {
        lock (_lock)
        {
            foreach (var hud in _activeHuds.ToList())
            {
                try
                {
                    hud.Window.Dispatcher.BeginInvoke(() => hud.Window.Close());
                }
                catch
                {
                }
            }
            _activeHuds.Clear();
        }
    }
}

public class HudInstance
{
    private readonly ILoggingService _logger = LoggingService.Instance;
    public HudViewModel ViewModel { get; }
    public HudWindow Window { get; }
    private readonly HudManager _manager;
    private bool _isClosing;
    private HudState? _lastAnimatedState;

    public HudInstance(HudViewModel viewModel, HudWindow window, HudManager manager)
    {
        ViewModel = viewModel;
        Window = window;
        _manager = manager;
    }

    public void Show()
    {
        Window.Dispatcher.BeginInvoke(() =>
        {
            Window.ShowHud();
            Window.Opacity = 0;
            
            Window.Loaded += (s, e) =>
            {
                _manager.UpdatePositions();
                var animation = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
                Window.BeginAnimation(UIElement.OpacityProperty, animation);
            };
            
            if (Window.IsLoaded)
            {
                _manager.UpdatePositions();
                var animation = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
                Window.BeginAnimation(UIElement.OpacityProperty, animation);
            }
        });
    }

    public void HideWithAnimation()
    {
        if (_isClosing) return;
        _isClosing = true;
        
        _logger.Info($"HideWithAnimation called for HUD");

        try
        {
            Window.Dispatcher.BeginInvoke(() =>
            {
                var animation = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
                animation.Completed += (s, e) =>
                {
                    try
                    {
                        Window.HideHud();
                        Window.Close();
                        _logger.Info($"HUD closed after animation");
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"Failed to close HUD: {ex.Message}");
                    }
                };
                Window.BeginAnimation(UIElement.OpacityProperty, animation);
            });
        }
        catch (Exception ex)
        {
            _logger.Error($"HideWithAnimation failed: {ex.Message}", ex);
        }
    }

    public void UpdateState(HudState state, string text)
    {
        try
        {
            Window.Dispatcher.BeginInvoke(() =>
            {
                var stateChanged = _lastAnimatedState != state;
                ViewModel.State = state;
                ViewModel.DisplayText = text;
                if (stateChanged)
                {
                    Window.AnimateStateChange();
                    _lastAnimatedState = state;
                }
            });
        }
        catch (Exception ex)
        {
            _logger.Warning($"Failed to update HUD state: {ex.Message}");
        }
    }

    public void UpdateAudioLevel(float level)
    {
        try
        {
            Window.Dispatcher.BeginInvoke(() =>
            {
                ViewModel.AudioLevel = level;
            });
        }
        catch
        {
        }
    }
}
