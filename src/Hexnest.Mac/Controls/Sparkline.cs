using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Hexnest.Mac.Controls;

/// <summary>
/// A small filled line chart of the last N readings.
///
/// Drawn directly rather than with a charting package, for the same reason the rest of
/// the project avoids dependencies: this is forty lines of geometry, and a chart
/// library that redraws once a second in a window that is already measuring the
/// machine's processor usage would be showing up in its own process table.
///
/// The vertical scale is fixed at 0-100 rather than fitted to the data. An autoscaling
/// sparkline is actively misleading for a load reading: a machine idling between 1% and
/// 3% would draw the same dramatic mountain range as one thrashing between 40% and 90%.
/// </summary>
public sealed class Sparkline : Control
{
    public static readonly StyledProperty<IReadOnlyList<double>?> ValuesProperty =
        AvaloniaProperty.Register<Sparkline, IReadOnlyList<double>?>(nameof(Values));

    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<Sparkline, IBrush?>(nameof(Stroke));

    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<Sparkline, double>(nameof(StrokeThickness), 1.6);

    /// <summary>Highest value the chart will plot. Readings above it are clamped.</summary>
    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<Sparkline, double>(nameof(Maximum), 100d);

    static Sparkline()
    {
        AffectsRender<Sparkline>(ValuesProperty, StrokeProperty, StrokeThicknessProperty, MaximumProperty);
    }

    public IReadOnlyList<double>? Values
    {
        get => GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public double Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var values = Values;
        if (values is null || values.Count < 2) return;

        double width = Bounds.Width;
        double height = Bounds.Height;
        if (width <= 1 || height <= 1) return;

        var stroke = Stroke ?? Brushes.Gray;
        double maximum = Maximum > 0 ? Maximum : 100;

        // One horizontal step per reading, with the newest at the right edge, so the
        // chart scrolls the way every other one does.
        double step = width / (values.Count - 1);

        var line = new StreamGeometry();

        using (var draw = line.Open())
        {
            for (int i = 0; i < values.Count; i++)
            {
                double x = i * step;
                double y = height - Math.Clamp(values[i] / maximum, 0, 1) * height;
                var point = new Point(x, y);

                if (i == 0) draw.BeginFigure(point, isFilled: false);
                else draw.LineTo(point);
            }

            draw.EndFigure(false);
        }

        // The fill is the same colour at a tenth of the opacity: enough to read the
        // area at a glance, not enough to compete with the line itself.
        var area = new StreamGeometry();

        using (var draw = area.Open())
        {
            draw.BeginFigure(new Point(0, height), isFilled: true);

            for (int i = 0; i < values.Count; i++)
            {
                double x = i * step;
                double y = height - Math.Clamp(values[i] / maximum, 0, 1) * height;
                draw.LineTo(new Point(x, y));
            }

            draw.LineTo(new Point(width, height));
            draw.EndFigure(true);
        }

        if (stroke is ISolidColorBrush solid)
        {
            var tint = new SolidColorBrush(solid.Color) { Opacity = 0.14 };
            context.DrawGeometry(tint, null, area);
        }

        context.DrawGeometry(null, new Pen(stroke, StrokeThickness, lineCap: PenLineCap.Round,
            lineJoin: PenLineJoin.Round), line);
    }
}

/// <summary>
/// The per-core strip: one small vertical bar per logical processor.
///
/// A separate control rather than a panel of rectangles because there can be twenty of
/// them redrawing once a second, and twenty templated controls to show twenty
/// rectangles is a lot of visual tree for what is one drawing call.
/// </summary>
public sealed class CoreStrip : Control
{
    public static readonly StyledProperty<IReadOnlyList<double>?> ValuesProperty =
        AvaloniaProperty.Register<CoreStrip, IReadOnlyList<double>?>(nameof(Values));

    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<CoreStrip, IBrush?>(nameof(Fill));

    public static readonly StyledProperty<IBrush?> TrackProperty =
        AvaloniaProperty.Register<CoreStrip, IBrush?>(nameof(Track));

    static CoreStrip()
    {
        AffectsRender<CoreStrip>(ValuesProperty, FillProperty, TrackProperty);
    }

    public IReadOnlyList<double>? Values
    {
        get => GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public IBrush? Track
    {
        get => GetValue(TrackProperty);
        set => SetValue(TrackProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var values = Values;
        if (values is null || values.Count == 0) return;

        double width = Bounds.Width;
        double height = Bounds.Height;
        if (width <= 1 || height <= 1) return;

        const double gap = 3;
        double barWidth = (width - gap * (values.Count - 1)) / values.Count;
        if (barWidth <= 0) return;

        var fill = Fill ?? Brushes.Gray;
        var track = Track;

        for (int i = 0; i < values.Count; i++)
        {
            double x = i * (barWidth + gap);
            double filled = Math.Clamp(values[i] / 100.0, 0, 1) * height;

            if (track is not null)
            {
                context.DrawRectangle(track, null,
                    new RoundedRect(new Rect(x, 0, barWidth, height), 2));
            }

            if (filled > 0.5)
            {
                context.DrawRectangle(fill, null,
                    new RoundedRect(new Rect(x, height - filled, barWidth, filled), 2));
            }
        }
    }
}
