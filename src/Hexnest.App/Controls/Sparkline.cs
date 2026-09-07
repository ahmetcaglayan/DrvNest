using System.Windows;
using System.Windows.Media;

namespace Hexnest.App.Controls;

/// <summary>
/// A rolling line chart for one metric.
///
/// Written as a <see cref="FrameworkElement"/> with a hand rolled OnRender rather than
/// a templated control over a Polyline, for two reasons. It redraws roughly once a
/// second for as long as the page is open, and a Polyline would rebuild a PointCollection
/// and re-run layout every time; and Hexnest ships as a single self-contained executable,
/// so a charting package is exactly the kind of weight this project refuses to add.
///
/// The buffer is a fixed size ring: values are pushed at the right and the oldest one
/// falls off the left, so memory never grows no matter how long the page stays open.
/// </summary>
public sealed class Sparkline : FrameworkElement
{
    private const int DefaultCapacity = 60;

    private double[] _values = new double[DefaultCapacity];
    private int _count;
    private int _head;

    // Rebuilt only when the geometry actually changes, then frozen so rendering is
    // cheap and thread safe.
    private Pen? _strokePen;
    private Brush? _fillBrush;

    // =====================================================================================
    // Properties
    // =====================================================================================

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(Sparkline),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender, OnPenChanged));

    public Brush Stroke
    {
        get => (Brush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness), typeof(double), typeof(Sparkline),
        new FrameworkPropertyMetadata(1.6, FrameworkPropertyMetadataOptions.AffectsRender, OnPenChanged));

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    /// <summary>Top of the value axis. 100 for a percentage; auto-scaled when 0.</summary>
    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(Sparkline),
        new FrameworkPropertyMetadata(100.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    /// <summary>How many samples are kept. Changing it clears the history.</summary>
    public static readonly DependencyProperty CapacityProperty = DependencyProperty.Register(
        nameof(Capacity), typeof(int), typeof(Sparkline),
        new FrameworkPropertyMetadata(DefaultCapacity, OnCapacityChanged));

    public int Capacity
    {
        get => (int)GetValue(CapacityProperty);
        set => SetValue(CapacityProperty, value);
    }

    /// <summary>Fills the area under the line at 18% opacity. On by default.</summary>
    public static readonly DependencyProperty ShowFillProperty = DependencyProperty.Register(
        nameof(ShowFill), typeof(bool), typeof(Sparkline),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender, OnPenChanged));

    public bool ShowFill
    {
        get => (bool)GetValue(ShowFillProperty);
        set => SetValue(ShowFillProperty, value);
    }

    /// <summary>Faint horizontal rules at 25/50/75%. On by default.</summary>
    public static readonly DependencyProperty ShowGridProperty = DependencyProperty.Register(
        nameof(ShowGrid), typeof(bool), typeof(Sparkline),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public bool ShowGrid
    {
        get => (bool)GetValue(ShowGridProperty);
        set => SetValue(ShowGridProperty, value);
    }

    /// <summary>
    /// The newest value. Setting it pushes a sample, which is what lets the whole chart
    /// be driven from XAML with a single one-way binding.
    /// </summary>
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(Sparkline),
        new FrameworkPropertyMetadata(0.0, OnValueChanged));

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    // =====================================================================================

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Sparkline chart && e.NewValue is double value) chart.Push(value);
    }

    private static void OnPenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Sparkline chart)
        {
            chart._strokePen = null;
            chart._fillBrush = null;
        }
    }

    private static void OnCapacityChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Sparkline chart || e.NewValue is not int capacity) return;

        chart._values = new double[Math.Clamp(capacity, 8, 4096)];
        chart._count = 0;
        chart._head = 0;
        chart.InvalidateVisual();
    }

    /// <summary>Appends a sample and repaints.</summary>
    public void Push(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) value = 0;

        _values[_head] = value;
        _head = (_head + 1) % _values.Length;
        if (_count < _values.Length) _count++;

        InvalidateVisual();
    }

    /// <summary>Drops every sample, e.g. when the page is reopened.</summary>
    public void Clear()
    {
        _count = 0;
        _head = 0;
        InvalidateVisual();
    }

    // =====================================================================================

    protected override void OnRender(DrawingContext context)
    {
        double width = ActualWidth;
        double height = ActualHeight;

        if (width <= 1 || height <= 1) return;

        // A transparent background rectangle gives the element a hit-test surface, so a
        // tooltip on the chart works.
        context.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, height));

        if (ShowGrid) DrawGrid(context, width, height);

        if (_count < 2) return;

        EnsureBrushes();

        // An auto-scaled chart (Maximum = 0) is normalised to its own peak, with a 15%
        // head room so a flat line does not sit against the top edge.
        double max = Maximum > 0 ? Maximum : Peak() * 1.15;
        if (max <= 0) max = 1;

        // The line always occupies the full width: with a partly filled buffer the
        // samples spread out, so an opening chart grows from the left instead of
        // squeezing itself into a corner.
        double step = width / (_values.Length - 1);
        double firstX = width - (_count - 1) * step;

        var line = new StreamGeometry();

        using (var draw = line.Open())
        {
            var start = new Point(firstX, Y(Read(0), max, height));
            draw.BeginFigure(start, false, false);

            for (int i = 1; i < _count; i++)
                draw.LineTo(new Point(firstX + i * step, Y(Read(i), max, height)), true, false);
        }

        line.Freeze();

        if (ShowFill && _fillBrush is not null)
        {
            var area = new StreamGeometry();

            using (var draw = area.Open())
            {
                draw.BeginFigure(new Point(firstX, height), true, true);

                for (int i = 0; i < _count; i++)
                    draw.LineTo(new Point(firstX + i * step, Y(Read(i), max, height)), true, false);

                draw.LineTo(new Point(firstX + (_count - 1) * step, height), true, false);
            }

            area.Freeze();
            context.DrawGeometry(_fillBrush, null, area);
        }

        context.DrawGeometry(null, _strokePen, line);
    }

    private void DrawGrid(DrawingContext context, double width, double height)
    {
        var brush = Stroke as SolidColorBrush;
        var color = brush?.Color ?? Colors.Gray;

        var pen = new Pen(new SolidColorBrush(Color.FromArgb(28, color.R, color.G, color.B)), 1);
        pen.Freeze();

        for (int i = 1; i <= 3; i++)
        {
            // Snapped to the pixel grid so a one pixel rule does not render as a two
            // pixel smear on a fractional DPI scale.
            double y = Math.Round(height * i / 4) + 0.5;
            context.DrawLine(pen, new Point(0, y), new Point(width, y));
        }
    }

    private void EnsureBrushes()
    {
        if (_strokePen is null)
        {
            _strokePen = new Pen(Stroke, StrokeThickness)
            {
                LineJoin = PenLineJoin.Round,
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };

            _strokePen.Freeze();
        }

        if (_fillBrush is null && ShowFill)
        {
            var color = (Stroke as SolidColorBrush)?.Color ?? Colors.Gray;

            var gradient = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, 1),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(64, color.R, color.G, color.B), 0),
                    new GradientStop(Color.FromArgb(6, color.R, color.G, color.B), 1)
                }
            };

            gradient.Freeze();
            _fillBrush = gradient;
        }
    }

    /// <summary>Reads the ring buffer oldest-first.</summary>
    private double Read(int index)
    {
        int start = _count == _values.Length ? _head : 0;
        return _values[(start + index) % _values.Length];
    }

    private double Peak()
    {
        double peak = 0;
        for (int i = 0; i < _count; i++) peak = Math.Max(peak, Read(i));
        return peak;
    }

    private static double Y(double value, double max, double height)
    {
        double ratio = Math.Clamp(value / max, 0, 1);

        // One pixel of padding at each edge keeps a 100% line from being clipped by the
        // stroke thickness.
        return height - 1 - ratio * (height - 2);
    }
}
