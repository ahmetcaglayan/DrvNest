using Hexnest.Core.Diagnostics;

namespace Hexnest.Core.Cleanup;

/// <summary>
/// Working set trimming, which macOS does not have and does not need.
///
/// The Windows version of this class asks the kernel to page out every process' working
/// set, and is honest in its own documentation that this does not make anything faster.
/// On macOS there is no equivalent at all: <c>EmptyWorkingSet</c> has no counterpart
/// that one process may call on another, and the memory compressor already does the job
/// continuously and better - which is why "free memory" on a Mac is close to zero on a
/// perfectly healthy machine and why that is not a problem to solve.
///
/// The class exists so that code shared with the Windows build compiles, and
/// <see cref="IsSupported"/> is how the clean-up page knows not to offer the card at
/// all rather than offering a button that does nothing.
/// </summary>
public sealed class MemoryTrimmer
{
    /// <summary>False on macOS. The page hides the memory card when this is false.</summary>
    public static bool IsSupported => false;

    /// <summary>Does nothing and reports that it did nothing.</summary>
    public MemoryTrimResult Trim(bool includeFileCache = true)
    {
        Log.Debug("Working set trimming was requested; macOS has no equivalent, so nothing was done.");
        return new MemoryTrimResult(0, 0, 0, 0);
    }
}
