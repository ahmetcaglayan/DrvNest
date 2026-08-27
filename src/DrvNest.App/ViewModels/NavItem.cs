using System.Windows.Controls;
using DrvNest.App.Services;

namespace DrvNest.App.ViewModels;

/// <summary>
/// One entry in the sidebar.
///
/// Views are created lazily through a factory so that opening DrvNest does not
/// build nine pages up front, and adding a tenth menu item is a single line in
/// <see cref="MainViewModel.BuildNavigation"/>.
/// </summary>
public sealed class NavItem : ViewModelBase
{
    private readonly Func<UserControl> _factory;
    private UserControl? _view;

    private int _badgeCount;
    private bool _isSelected;

    public NavItem(string key, string labelKey, string glyph, Func<UserControl> factory)
    {
        Key = key;
        LabelKey = labelKey;
        Glyph = glyph;
        _factory = factory;
    }

    /// <summary>Stable identifier used by <see cref="AppEvents.RequestNavigation"/>.</summary>
    public string Key { get; }

    /// <summary>Localisation key, resolved on demand so a language switch is free.</summary>
    public string LabelKey { get; }

    /// <summary>Segoe MDL2 Assets glyph shown to the left of the label.</summary>
    public string Glyph { get; }

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
    public UserControl View => _view ??= _factory();

    /// <summary>Re-reads the label after a language change.</summary>
    public void RefreshLabel() => Raise(nameof(Label));

    /// <summary>
    /// Drops the cached page so it is rebuilt in the new language.
    /// Loses page-local state such as scroll position, which is an acceptable cost
    /// for an action the user takes deliberately and rarely.
    /// </summary>
    public void ResetView()
    {
        _view = null;
        Raise(nameof(View));
    }
}
