using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ZapretWrapper.Controls;

public partial class MetricRing : UserControl
{
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(string), typeof(MetricRing),
            new PropertyMetadata("0", OnLayoutChanged));

    public static readonly DependencyProperty CaptionProperty =
        DependencyProperty.Register(nameof(Caption), typeof(string), typeof(MetricRing),
            new PropertyMetadata("", OnLayoutChanged));

    public static readonly DependencyProperty ArcBrushProperty =
        DependencyProperty.Register(nameof(ArcBrush), typeof(Brush), typeof(MetricRing),
            new PropertyMetadata(Brushes.SteelBlue, OnLayoutChanged));

    public static readonly DependencyProperty ProgressProperty =
        DependencyProperty.Register(nameof(Progress), typeof(double), typeof(MetricRing),
            new PropertyMetadata(0.0, OnLayoutChanged));

    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public string Caption
    {
        get => (string)GetValue(CaptionProperty);
        set => SetValue(CaptionProperty, value);
    }

    public Brush ArcBrush
    {
        get => (Brush)GetValue(ArcBrushProperty);
        set => SetValue(ArcBrushProperty, value);
    }

    /// <summary>0..1</summary>
    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public MetricRing()
    {
        InitializeComponent();
        Rebuild();
    }

    private static void OnLayoutChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MetricRing ring) ring.Rebuild();
    }

    private void Rebuild()
    {
        if (ArcPath is null) return;

        const double size = 160;
        const double stroke = 8;
        const double radius = (size - stroke) / 2.0;
        var center = new Point(size / 2.0, size / 2.0);

        var clamped = Math.Max(0, Math.Min(1, Progress));

        if (clamped <= 0.0001)
        {
            ArcPath.Data = null;
            return;
        }

        // Старт от "12 часов" = (center.X, center.Y - radius).
        // Дуга идёт по часовой стрелке на угол 2π·clamped.
        var start = new Point(center.X, center.Y - radius);
        var endAngle = -Math.PI / 2.0 + 2 * Math.PI * clamped;
        var end = new Point(
            center.X + radius * Math.Cos(endAngle),
            center.Y + radius * Math.Sin(endAngle));

        var figure = new PathFigure
        {
            StartPoint = start,
            IsClosed = false,
            IsFilled = false
        };
        figure.Segments.Add(new ArcSegment(
            end,
            new Size(radius, radius),
            0,
            clamped > 0.5,
            SweepDirection.Clockwise,
            isStroked: true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        geometry.Freeze();

        ArcPath.Data = geometry;
    }
}
