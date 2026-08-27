using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using DrvNest.App.Services;
using DrvNest.Core.Models;

namespace DrvNest.App.Converters;

/// <summary>true -&gt; Visible, false -&gt; Collapsed. Pass "invert" to flip it.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool flag = value is bool b && b;
        if (parameter is string s && s.Equals("invert", StringComparison.OrdinalIgnoreCase)) flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Visibility v && v == Visibility.Visible;
}

/// <summary>Shows an element only when a collection or string has content.</summary>
public sealed class EmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool hasContent = value switch
        {
            null => false,
            string s => s.Length > 0,
            System.Collections.ICollection c => c.Count > 0,
            int i => i > 0,
            double d => d > 0,
            _ => true
        };

        if (parameter is string p && p.Equals("invert", StringComparison.OrdinalIgnoreCase))
            hasContent = !hasContent;

        return hasContent ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>Null -&gt; Collapsed.</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool isNull = value is null || (value is string s && s.Length == 0);
        if (parameter is string p && p.Equals("invert", StringComparison.OrdinalIgnoreCase)) isNull = !isNull;
        return isNull ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>Looks a key up in the localisation table. Used as {Binding Source=..., Converter=...}.</summary>
public sealed class LocalizeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Loc.T(value?.ToString() ?? string.Empty);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>Maps a device health value onto a semantic brush.</summary>
public sealed class HealthToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value switch
        {
            DeviceHealth.DriverMissing => "Brush.Danger",
            DeviceHealth.Faulty => "Brush.Warning",
            DeviceHealth.RestartPending => "Brush.Info",
            DeviceHealth.Disabled => "Brush.TextFaint",
            _ => "Brush.Success"
        };

        return Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>Maps a job state onto a semantic brush.</summary>
public sealed class JobStateToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value switch
        {
            JobState.Succeeded => "Brush.Success",
            JobState.RebootRequired => "Brush.Info",
            JobState.Failed => "Brush.Danger",
            JobState.Cancelled => "Brush.TextFaint",
            JobState.Downloading or JobState.Installing => "Brush.Accent",
            JobState.PendingResume => "Brush.Warning",
            _ => "Brush.TextMuted"
        };

        return Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>Turns a job state into a short, localised label.</summary>
public sealed class JobStateToTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool turkish = Loc.Language == "tr";

        return value switch
        {
            JobState.Queued => turkish ? "Sırada" : "Queued",
            JobState.Downloading => turkish ? "İndiriliyor" : "Downloading",
            JobState.Downloaded => turkish ? "İndirildi" : "Downloaded",
            JobState.Installing => turkish ? "Kuruluyor" : "Installing",
            JobState.Succeeded => turkish ? "Tamamlandı" : "Done",
            JobState.Failed => turkish ? "Başarısız" : "Failed",
            JobState.Cancelled => turkish ? "İptal edildi" : "Cancelled",
            JobState.RebootRequired => turkish ? "Yeniden başlatma gerekli" : "Restart required",
            JobState.PendingResume => turkish ? "Yeniden başlatma bekleniyor" : "Waiting for restart",
            _ => value?.ToString() ?? string.Empty
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>Maps a history outcome onto a semantic brush.</summary>
public sealed class OutcomeToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value switch
        {
            HistoryOutcome.Success => "Brush.Success",
            HistoryOutcome.Failed => "Brush.Danger",
            HistoryOutcome.RolledBack => "Brush.Warning",
            _ => "Brush.TextFaint"
        };

        return Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>
/// Reduces a device class to a one or two letter avatar.
///
/// Deliberately not an icon font: this converter runs for every row of a list that
/// can hold several hundred devices, and a glyph missing from the installed font
/// would render as an empty box on every one of them. Letters always render.
/// </summary>
public sealed class DeviceClassToInitialConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var name = value?.ToString()?.Trim();
        if (string.IsNullOrEmpty(name)) return "?";

        var words = name.Split(new[] { ' ', '-', '/', '(' }, StringSplitOptions.RemoveEmptyEntries);

        if (words.Length >= 2 && words[0].Length > 0 && words[1].Length > 0)
            return string.Concat(char.ToUpperInvariant(words[0][0]), char.ToUpperInvariant(words[1][0]));

        return name.Length >= 2 ? name[..2].ToUpperInvariant() : name[..1].ToUpperInvariant();
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>
/// Derives a stable accent colour from a device class name, so a class keeps the
/// same avatar colour between runs without a hard-coded lookup table.
/// </summary>
public sealed class DeviceClassToColorConverter : IValueConverter
{
    private static readonly string[] Palette =
    {
        "#4C8DFF", "#35D8A4", "#F5A623", "#FF5F6B",
        "#A78BFA", "#22C7D6", "#F472B6", "#84CC16"
    };

    private static readonly Dictionary<string, Brush> Cache = new(StringComparer.Ordinal);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var name = value?.ToString() ?? string.Empty;

        lock (Cache)
        {
            if (Cache.TryGetValue(name, out var cached)) return cached;

            int hash = 17;
            foreach (var c in name) hash = unchecked(hash * 31 + char.ToUpperInvariant(c));

            var color = (Color)ColorConverter.ConvertFromString(Palette[Math.Abs(hash) % Palette.Length])!;
            var brush = new SolidColorBrush(color);
            brush.Freeze();

            Cache[name] = brush;
            return brush;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>Multiplies a number, e.g. to derive a bar width from a percentage.</summary>
public sealed class ScaleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double number = value is IConvertible c ? c.ToDouble(CultureInfo.InvariantCulture) : 0;
        double factor = parameter is string s && double.TryParse(
            s, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : 1;

        return number * factor;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>Maps a log level onto a semantic brush so warnings and errors stand out.</summary>
public sealed class LogLevelToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value switch
        {
            Core.Models.LogLevel.Error => "Brush.Danger",
            Core.Models.LogLevel.Warn => "Brush.Warning",
            Core.Models.LogLevel.Debug => "Brush.TextFaint",
            _ => "Brush.TextMuted"
        };

        return Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>Turns a device health value into a short localised label.</summary>
public sealed class HealthToTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            DeviceHealth.DriverMissing => Loc.T("health.missing"),
            DeviceHealth.Faulty => Loc.T("health.faulty"),
            DeviceHealth.Disabled => Loc.T("health.disabled"),
            DeviceHealth.RestartPending => Loc.T("health.restart"),
            DeviceHealth.Healthy => Loc.T("health.healthy"),
            _ => value?.ToString() ?? string.Empty
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>
/// Localises a Configuration Manager problem.
///
/// Bound to the whole <see cref="DeviceItem"/> rather than to the code alone, so an
/// untranslated code can still fall back to the English sentence the scanner
/// produced instead of showing the user a bare number.
/// </summary>
public sealed class ProblemToTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DeviceItem device) return string.Empty;
        if (device.ProblemCode == 0 && device.Health != DeviceHealth.DriverMissing) return string.Empty;

        int code = device.ProblemCode == 0 ? 28 : device.ProblemCode;

        var key = $"prob.{code}";
        var text = Loc.T(key);

        // Loc.T returns the key itself when nothing matches.
        return text == key ? device.ProblemText ?? string.Empty : text;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>
/// Resolves a setup class GUID to the icon Windows itself uses for that class.
/// Returns null when there is none, which lets the view fall back to the lettered
/// avatar rather than showing a blank square.
/// </summary>
public sealed class ClassGuidToIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => DeviceIconProvider.Get(value?.ToString());

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>Maps a device's update state onto a semantic brush.</summary>
public sealed class UpdateStateToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value switch
        {
            ViewModels.DeviceUpdateState.UpdateAvailable => "Brush.Accent",
            ViewModels.DeviceUpdateState.NoDriver => "Brush.Danger",
            ViewModels.DeviceUpdateState.UpToDate => "Brush.Success",
            _ => "Brush.TextFaint"
        };

        return Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}
