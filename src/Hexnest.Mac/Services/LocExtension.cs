using Avalonia.Markup.Xaml;
using Hexnest.Core.Localization;

namespace Hexnest.Mac.Services;

/// <summary>
/// <c>{s:Loc nav.dashboard}</c> in markup.
///
/// Resolved once, at parse time, exactly as on Windows - which is why a language
/// change rebuilds the pages rather than re-binding them. That trade was made in the
/// WPF build and it holds here: a binding per string would cost more, in a monitor
/// that is already redrawing once a second, than rebuilding a page on the rare
/// occasion somebody switches language.
/// </summary>
public sealed class LocExtension : MarkupExtension
{
    public LocExtension()
    {
    }

    public LocExtension(string key) => Key = key;

    /// <summary>The localisation key, e.g. "common.cancel".</summary>
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
        => string.IsNullOrWhiteSpace(Key) ? string.Empty : Loc.T(Key);
}
