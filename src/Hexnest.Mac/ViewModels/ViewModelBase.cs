using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia.Threading;

namespace Hexnest.Mac.ViewModels;

/// <summary>Shared INotifyPropertyChanged plumbing.</summary>
public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }

    protected void Raise([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>Raises several properties at once, for computed values.</summary>
    protected void RaiseAll(params string[] names)
    {
        foreach (var name in names) Raise(name);
    }

    /// <summary>Marshals an action onto the UI thread. Safe to call from any thread.</summary>
    protected static void OnUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }
}

/// <summary>
/// Minimal ICommand. Deliberately not pulled from a MVVM framework, for the same
/// reason as on Windows: every avoided dependency is weight removed from what somebody
/// downloads.
///
/// The one real difference from the WPF version is why this file exists at all.
/// WPF has <c>CommandManager.RequerySuggested</c>, a global signal raised on every
/// input event that re-asks every command whether it can execute. Avalonia has no such
/// thing - and is right not to, because polling every command on every keystroke is a
/// strange way to spend a frame budget. <see cref="Requery"/> replaces it with an
/// explicit signal that the view models raise when something actually changed.
/// </summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = _ => execute();
        _canExecute = canExecute is null ? null : _ => canExecute();
    }

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add => Requery += value;
        remove => Requery -= value;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => _execute(parameter);

    /// <summary>
    /// The replacement for CommandManager.RequerySuggested. Every command in the
    /// application listens to it; anything that changes what a command can do raises it
    /// through <see cref="RaiseCanExecuteChanged"/>.
    /// </summary>
    internal static event EventHandler? Requery;

    public static void RaiseCanExecuteChanged()
    {
        if (Dispatcher.UIThread.CheckAccess()) Requery?.Invoke(null, EventArgs.Empty);
        else Dispatcher.UIThread.Post(() => Requery?.Invoke(null, EventArgs.Empty));
    }
}

/// <summary>
/// Async command that blocks re-entry while it runs and surfaces exceptions instead
/// of letting them escape onto the dispatcher as an unhandled crash.
/// </summary>
public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<object?, Task> _execute;
    private readonly Func<object?, bool>? _canExecute;
    private readonly Action<Exception>? _onError;

    private bool _isRunning;

    public AsyncRelayCommand(
        Func<Task> execute,
        Func<bool>? canExecute = null,
        Action<Exception>? onError = null)
    {
        _execute = _ => execute();
        _canExecute = canExecute is null ? null : _ => canExecute();
        _onError = onError;
    }

    public AsyncRelayCommand(
        Func<object?, Task> execute,
        Func<object?, bool>? canExecute = null,
        Action<Exception>? onError = null)
    {
        _execute = execute;
        _canExecute = canExecute;
        _onError = onError;
    }

    public event EventHandler? CanExecuteChanged
    {
        add => RelayCommand.Requery += value;
        remove => RelayCommand.Requery -= value;
    }

    public bool IsRunning => _isRunning;

    public bool CanExecute(object? parameter)
        => !_isRunning && (_canExecute?.Invoke(parameter) ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;

        _isRunning = true;
        RelayCommand.RaiseCanExecuteChanged();

        try
        {
            await _execute(parameter).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Cancelling is a normal outcome, not an error.
        }
        catch (Exception ex)
        {
            Core.Diagnostics.Log.Error("Command failed", ex);
            _onError?.Invoke(ex);
        }
        finally
        {
            _isRunning = false;
            RelayCommand.RaiseCanExecuteChanged();
        }
    }
}
