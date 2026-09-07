using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Hexnest.Core.Models;

namespace Hexnest.Mac.Converters;

/// <summary>
/// Turns an icon resource key into the geometry it names.
///
/// The view models carry a key rather than a <see cref="Geometry"/> so that nothing in
/// them has to reach into the application's resources, which is the sort of coupling
/// that makes a view model impossible to think about on its own.
/// </summary>
public sealed class IconLookupConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string key || key.Length == 0) return null;

        return Lookup(key);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static object? Lookup(string key) => ResourceLookup.Find(key);
}

/// <summary>Formats a byte count the way the rest of Hexnest does.</summary>
public sealed class BytesConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            long bytes => Formatting.HumanBytes(bytes),
            double bytes => Formatting.HumanBytes((long)bytes),
            int bytes => Formatting.HumanBytes(bytes),
            _ => "-"
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Formats a rate as "12.4 MB/s".</summary>
public sealed class RateConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is double rate ? Formatting.HumanBytes((long)rate) + "/s" : "-";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Inverts a boolean, for the many places where a control is visible exactly when
/// something is not happening.
/// </summary>
public sealed class NotConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool flag && !flag;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool flag && !flag;
}

/// <summary>True when a string actually has something in it.</summary>
public sealed class HasTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => !string.IsNullOrWhiteSpace(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Colours a 0-100 reading: calm below two thirds, amber approaching the limit, red at
/// it.
///
/// The thresholds are generous on purpose. A machine sitting at 70% memory is a machine
/// using the memory it was bought with, and a monitor that paints that red teaches its
/// user to ignore the colour.
/// </summary>
public sealed class LoadBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double percent = value switch
        {
            double d => d,
            int i => i,
            long l => l,
            _ => 0
        };

        var key = percent switch
        {
            >= 90 => "Brush.Danger",
            >= 75 => "Brush.Warning",
            _ => "Brush.Accent"
        };

        return Lookup(key);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static object? Lookup(string key) => ResourceLookup.Find(key);
}

/// <summary>
/// Resolves a key against the application's own resources.
///
/// Avalonia's TryGetResource wants a theme variant, and the right one to ask for is
/// whatever the window is currently showing rather than a fixed Light or Dark - so
/// that a brush resolved here follows a theme switch like every other brush does.
/// </summary>
internal static class ResourceLookup
{
    public static object? Find(string key)
    {
        var application = Application.Current;
        if (application is null) return null;

        return application.TryGetResource(key, application.ActualThemeVariant, out var value)
            ? value
            : null;
    }
}

/// <summary>Shared instances, so the markup does not construct one per binding.</summary>
public static class Convert
{
    public static readonly IconLookupConverter Icon = new();
    public static readonly BytesConverter Bytes = new();
    public static readonly RateConverter Rate = new();
    public static readonly NotConverter Not = new();
    public static readonly HasTextConverter HasText = new();
    public static readonly LoadBrushConverter LoadBrush = new();
}
