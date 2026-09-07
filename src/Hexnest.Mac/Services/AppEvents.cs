namespace Hexnest.Mac.Services;

/// <summary>
/// The few pieces of state that genuinely have to cross view boundaries.
///
/// A tiny event bus, exactly as on Windows: any page may want to send the user
/// somewhere else, and the shell owns the status line that several pages write to.
/// The Windows version also carries the scan and queue events, which do not exist on
/// a platform with no driver store, so this one is shorter.
/// </summary>
public static class AppEvents
{
    /// <summary>Raised while a long operation reports progress.</summary>
    public static event Action<string>? StatusChanged;

    /// <summary>Raised when a page asks the shell to switch to another page.</summary>
    public static event Action<string>? NavigationRequested;

    /// <summary>Raised when the user changes the language, so views can re-read strings.</summary>
    public static event Action? LanguageChanged;

    public static void RaiseStatus(string message) => StatusChanged?.Invoke(message);

    public static void RequestNavigation(string key) => NavigationRequested?.Invoke(key);

    public static void RaiseLanguageChanged() => LanguageChanged?.Invoke();
}
