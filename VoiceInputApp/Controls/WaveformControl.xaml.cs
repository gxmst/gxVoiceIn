using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace VoiceInputApp.Controls;

public partial class WaveformControl : UserControl
{
    public static readonly DependencyProperty LevelsProperty =
        DependencyProperty.Register(nameof(Levels), typeof(float[]), typeof(WaveformControl),
            new PropertyMetadata(new float[] { 0, 0, 0, 0, 0 }, OnLevelsChanged));

    private readonly Rectangle[] _bars = new Rectangle[5];
    private readonly float[] _weights = { 0.5f, 0.8f, 1.0f, 0.75f, 0.55f };
    private readonly float[] _smoothedLevels = new float[5];

    public float[] Levels
    {
        get => (float[])GetValue(LevelsProperty);
        set => SetValue(LevelsProperty, value);
    }

    public WaveformControl()
    {
        InitializeComponent();
        CreateBars();
    }

    private void CreateBars()
    {
        for (var i = 0; i < 5; i++)
        {
            _bars[i] = new Rectangle
            {
                Fill = new SolidColorBrush(Color.FromRgb(255, 255, 255)),
                Width = 4,
                RadiusX = 2,
                RadiusY = 2,
                VerticalAlignment = VerticalAlignment.Center
            };
            Container.Children.Add(_bars[i]);

            if (i < 4)
            {
                Container.Children.Add(new Border { Width = 4 });
            }
        }
    }

    private static void OnLevelsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is WaveformControl control && e.NewValue is float[] levels)
        {
            control.UpdateBars(levels);
        }
    }

    private void UpdateBars(float[] levels)
    {
        if (levels == null || levels.Length < 5) return;

        for (var i = 0; i < 5; i++)
        {
            var targetHeight = Math.Max(4, levels[i] * 28 * _weights[i]);

            _smoothedLevels[i] = _smoothedLevels[i] * 0.6f + targetHeight * 0.4f;

            _bars[i].Height = _smoothedLevels[i];
        }
    }
}
