using DrvNest.Core.Models;

namespace DrvNest.App.Services;

/// <summary>
/// The few pieces of state that genuinely have to cross view boundaries.
///
/// A scan is owned by the shell but consumed by four different pages, and any page
/// may want to send the user somewhere else ("2 drivers are missing" -> Updates).
/// A tiny event bus keeps those relationships explicit instead of threading view
/// model references through every constructor.
/// </summary>
public static class AppEvents
{
    /// <summary>Raised on the UI thread after a scan finishes.</summary>
    public static event Action<ScanResult>? ScanCompleted;

    /// <summary>Raised while a long operation reports progress.</summary>
    public static event Action<string>? StatusChanged;

    /// <summary>Raised when a page asks the shell to switch to another page.</summary>
    public static event Action<string>? NavigationRequested;

    /// <summary>Raised when the queue starts, changes or finishes.</summary>
    public static event Action? QueueChanged;

    /// <summary>Raised when the user changes the language, so views can re-read strings.</summary>
    public static event Action? LanguageChanged;

    public static void RaiseScanCompleted(ScanResult result) => ScanCompleted?.Invoke(result);

    public static void RaiseStatus(string message) => StatusChanged?.Invoke(message);

    public static void RequestNavigation(string key) => NavigationRequested?.Invoke(key);

    public static void RaiseQueueChanged() => QueueChanged?.Invoke();

    public static void RaiseLanguageChanged() => LanguageChanged?.Invoke();
}
