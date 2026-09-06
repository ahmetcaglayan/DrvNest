using System.Collections.ObjectModel;

namespace DrvNest.App.ViewModels;

/// <summary>
/// Patches an <see cref="ObservableCollection{T}"/> so it matches a desired order,
/// using the fewest possible change notifications.
///
/// The monitor pages rebuild their sort order roughly once a second. Clearing the
/// collection and re-adding everything would be one line, and it would also reset the
/// scroll position, drop the selection and make the list flicker every single second -
/// which is precisely what makes a live table unusable. Removing, inserting and moving
/// only what actually changed keeps the rows the user is reading exactly where they are.
///
/// Rows are compared by reference on purpose: the caller reuses one row object per
/// process for the lifetime of that process, so a row that merely changed its numbers
/// is the same object and never moves unless its rank did.
/// </summary>
internal static class ListSync
{
    public static void Apply<T>(ObservableCollection<T> target, IReadOnlyList<T> desired)
        where T : class
    {
        // Fast path: an unchanged list is the common case while nothing is happening.
        if (target.Count == desired.Count)
        {
            bool identical = true;

            for (int i = 0; i < desired.Count; i++)
            {
                if (!ReferenceEquals(target[i], desired[i])) { identical = false; break; }
            }

            if (identical) return;
        }

        var wanted = new HashSet<T>(desired, ByReference<T>.Instance);

        for (int i = target.Count - 1; i >= 0; i--)
        {
            if (!wanted.Contains(target[i])) target.RemoveAt(i);
        }

        for (int i = 0; i < desired.Count; i++)
        {
            var item = desired[i];

            int current = -1;

            for (int j = i; j < target.Count; j++)
            {
                if (ReferenceEquals(target[j], item)) { current = j; break; }
            }

            if (current < 0) target.Insert(i, item);
            else if (current != i) target.Move(current, i);
        }

        // Anything left past the end was dropped from the desired list.
        while (target.Count > desired.Count) target.RemoveAt(target.Count - 1);
    }

    /// <summary>
    /// Identity comparison for a generic type.
    /// <see cref="ReferenceEqualityComparer"/> only implements
    /// <c>IEqualityComparer&lt;object&gt;</c>, so casting it to
    /// <c>IEqualityComparer&lt;T&gt;</c> silently yields null and quietly changes the
    /// comparison back to the default one.
    /// </summary>
    private sealed class ByReference<T> : IEqualityComparer<T> where T : class
    {
        public static readonly ByReference<T> Instance = new();

        public bool Equals(T? x, T? y) => ReferenceEquals(x, y);

        public int GetHashCode(T value) =>
            System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
    }
}
