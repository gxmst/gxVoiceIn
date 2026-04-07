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

    private readonly float[] _weights = { 0.4f, 0.9f, 1.2f, 0.9f, 0.4f };
    private readonly float[] _smoothedLevels = new float[5];
    private const double WidthPerPoint = 10;

    public float[] Levels
    {
        get => (float[])GetValue(LevelsProperty);
        set => SetValue(LevelsProperty, value);
    }

    public WaveformControl()
    {
        InitializeComponent();
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

        var points = new Point[5];
        for (var i = 0; i < 5; i++)
        {
            var targetHeight = levels[i] * 22 * _weights[i];
            _smoothedLevels[i] = _smoothedLevels[i] * 0.7f + targetHeight * 0.3f;
            
            // Adjust vertical center to be at Grid center
            points[i] = new Point(i * WidthPerPoint, 16 - _smoothedLevels[i]);
        }

        // Draw Liquid Curve
        var figure = new PathFigure { StartPoint = new Point(0, 16), IsClosed = false };
        
        // Add Quadratic Bezier segments for smoothness
        for (int i = 0; i < 4; i++)
        {
            var p1 = points[i];
            var p2 = points[i + 1];
            var mid = new Point((p1.X + p2.X) / 2, (p1.Y + p2.Y) / 2);
            
            figure.Segments.Add(new QuadraticBezierSegment(p1, mid, true));
        }
        figure.Segments.Add(new LineSegment(new Point(40, 16), true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        WavePath.Data = geometry;
    }
}
