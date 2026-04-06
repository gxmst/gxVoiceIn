using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using VoiceInputApp.Models;

namespace VoiceInputApp.ViewModels;

public class HudViewModel : INotifyPropertyChanged
{
    private HudState _state = HudState.Hidden;
    private string _displayText = string.Empty;
    private float _audioLevel;
    private bool _isVisible;

    public HudState State
    {
        get => _state;
        set
        {
            if (_state != value)
            {
                _state = value;
                OnPropertyChanged();
                UpdateVisibility();
            }
        }
    }

    public string DisplayText
    {
        get => _displayText;
        set
        {
            if (_displayText != value)
            {
                _displayText = value;
                OnPropertyChanged();
            }
        }
    }

    public float AudioLevel
    {
        get => _audioLevel;
        set
        {
            if (Math.Abs(_audioLevel - value) > 0.001f)
            {
                _audioLevel = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(WaveformLevels));
            }
        }
    }

    public bool IsVisible
    {
        get => _isVisible;
        private set
        {
            if (_isVisible != value)
            {
                _isVisible = value;
                OnPropertyChanged();
            }
        }
    }

    public float[] WaveformLevels
    {
        get
        {
            var weights = new[] { 0.5f, 0.8f, 1.0f, 0.75f, 0.55f };
            var random = new Random();
            var levels = new float[5];

            for (var i = 0; i < 5; i++)
            {
                var jitter = (float)(random.NextDouble() * 0.08 - 0.04);
                levels[i] = Math.Clamp(_audioLevel * weights[i] * (1 + jitter), 0f, 1f);
            }

            return levels;
        }
    }

    private void UpdateVisibility()
    {
        IsVisible = _state != HudState.Hidden;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
