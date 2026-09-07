using Avalonia.Controls;
using Hexnest.Core.Localization;

namespace Hexnest.Mac.ViewModels;

/// <summary>
/// One entry in the sidebar.
///
/// Views are created lazily through a factory so that opening Hexnest does not build
/// eight pages up front - which matters more here than on Windows, because two of
/// those pages start a monitor as soon as they exist.
/// </summary>
public sealed class NavItem : ViewModelBase
{
    private readonly Func<Control> _factory;
    private Control? _view;

    private int _badgeCount;
    private bool _isSelected;

    public NavItem(string key, string labelKey, string icon, Func<Control> factory)
    {
        Key = key;
        LabelKey = labelKey;
        Icon = icon;
        _factory = factory;
    }

    /// <summary>Stable identifier used by <see cref="Services.AppEvents.RequestNavigation"/>.</summary>
    public string Key { get; }

    /// <summary>Localisation key, resolved on demand so a language switch is free.</summary>
    public string LabelKey { get; }

    /// <summary>Resource key of the geometry in Themes/Icons.axaml.</summary>
    public string Icon { get; }

    public string Label => Loc.T(LabelKey);

    /// <summary>Small count bubble; 0 hides it.</summary>
    public int BadgeCount
    {
        get => _badgeCount;
        set { if (Set(ref _badgeCount, value)) Raise(nameof(HasBadge)); }
    }

    public bool HasBadge => BadgeCount > 0;

    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }

    /// <summary>Builds the page the first time it is shown, then reuses it.</summary>
    public Control View => _view ??= _factory();

    /// <summary>Re-reads the label after a language change.</summary>
    public void RefreshLabel() => Raise(nameof(Label));

    /// <summary>
    /// Drops the cached page so it is rebuilt in the new language. Loses page-local
    /// state such as scroll position, which is an acceptable cost for an action the
    /// user takes deliberately and rarely.
    /// </summary>
    public void ResetView()
    {
        (_view as IDisposable)?.Dispose();
        _view = null;
        Raise(nameof(View));
    }
}
