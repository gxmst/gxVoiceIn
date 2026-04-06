using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using VoiceInputApp.Models;

namespace VoiceInputApp;

public class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b)
            return !b;
        return value;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b)
            return !b;
        return value;
    }
}

public class StateToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is HudState state && parameter is string targetState)
        {
            return state.ToString() == targetState ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class HudStateToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not HudState state)
        {
            return new SolidColorBrush(Color.FromRgb(108, 117, 125));
        }

        return state switch
        {
            HudState.Listening => new SolidColorBrush(Color.FromRgb(67, 233, 123)),
            HudState.Transcribing => new SolidColorBrush(Color.FromRgb(77, 171, 247)),
            HudState.Refining => new SolidColorBrush(Color.FromRgb(255, 184, 77)),
            HudState.Success => new SolidColorBrush(Color.FromRgb(56, 217, 169)),
            HudState.Error => new SolidColorBrush(Color.FromRgb(255, 107, 107)),
            _ => new SolidColorBrush(Color.FromRgb(108, 117, 125))
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
