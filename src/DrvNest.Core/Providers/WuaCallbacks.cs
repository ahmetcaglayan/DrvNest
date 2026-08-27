using System.Runtime.InteropServices;

namespace DrvNest.Core.Providers;

/// <summary>
/// The managed objects WUA calls back into.
///
/// Each one adapts a COM callback onto a plain delegate or a TaskCompletionSource.
/// They are invoked on RPC threads, never on the UI thread, so a handler must be
/// cheap and must never let an exception escape back into COM: an exception crossing
/// that boundary becomes an HRESULT that aborts the whole download or install.
///
/// [ComVisible(true)] is explicit rather than inherited so the class keeps a usable
/// CCW no matter what the assembly-level default is.
/// </summary>
[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
internal sealed class SearchCompletedCallback : ISearchCompletedCallback
{
    private readonly TaskCompletionSource<bool> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<bool> Completed => _completion.Task;

    public void Invoke(object searchJob, object callbackArgs) => _completion.TrySetResult(true);

    /// <summary>Releases the waiter when the operation is abandoned.</summary>
    public void Release() => _completion.TrySetResult(false);
}

[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
internal sealed class DownloadProgressCallback : IDownloadProgressChangedCallback
{
    private readonly Action<dynamic> _onProgress;

    public DownloadProgressCallback(Action<dynamic> onProgress) => _onProgress = onProgress;

    public void Invoke(object downloadJob, object callbackArgs)
    {
        try
        {
            dynamic args = callbackArgs;
            _onProgress(args.Progress);
        }
        catch
        {
            // A progress hiccup must never fail the download it is reporting on.
        }
    }
}

[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
internal sealed class DownloadCompletedCallback : IDownloadCompletedCallback
{
    private readonly TaskCompletionSource<bool> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<bool> Completed => _completion.Task;

    public void Invoke(object downloadJob, object callbackArgs) => _completion.TrySetResult(true);

    public void Release() => _completion.TrySetResult(false);
}

[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
internal sealed class InstallProgressCallback : IInstallationProgressChangedCallback
{
    private readonly Action<dynamic> _onProgress;

    public InstallProgressCallback(Action<dynamic> onProgress) => _onProgress = onProgress;

    public void Invoke(object installationJob, object callbackArgs)
    {
        try
        {
            dynamic args = callbackArgs;
            _onProgress(args.Progress);
        }
        catch
        {
            // Same reasoning as the download callback.
        }
    }
}

[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
internal sealed class InstallCompletedCallback : IInstallationCompletedCallback
{
    private readonly TaskCompletionSource<bool> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<bool> Completed => _completion.Task;

    public void Invoke(object installationJob, object callbackArgs) => _completion.TrySetResult(true);

    public void Release() => _completion.TrySetResult(false);
}
