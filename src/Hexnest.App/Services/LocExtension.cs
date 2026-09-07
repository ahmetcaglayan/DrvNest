using System.Windows.Markup;

namespace Hexnest.App.Services;

/// <summary>
/// XAML markup extension for localised strings: <c>{s:Loc nav.updates}</c>.
///
/// Resolves once, at parse time. That is deliberate: switching language rebuilds the
/// pages (see <see cref="ViewModels.NavItem.ResetView"/>), which is both simpler and
/// more reliable than a live binding whose path would have to contain dotted keys
/// inside an indexer.
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class LocExtension : MarkupExtension
{
    public LocExtension()
    {
    }

    public LocExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
        => Loc.T(Key);
}
