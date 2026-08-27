using System.Runtime.InteropServices;

namespace DrvNest.Core.Providers;

/// <summary>
/// Hand written interop for the Windows Update Agent callbacks.
///
/// Why not a COMReference to WUApiLib? Because tlbimp only runs under the .NET
/// Framework build of MSBuild: `dotnet build` fails with MSB4803. Requiring Visual
/// Studio to compile an open source project - and to run CI - is a worse trade than
/// declaring five one-method interfaces by hand.
///
/// Every GUID below was read out of %SystemRoot%\System32\wuapi.dll with ITypeLib
/// rather than copied from memory or a blog post. All five interfaces derive from
/// IUnknown (confirmed: vtable offset 24 on x64, i.e. slot 3) and declare exactly one
/// method, Invoke, taking two interface pointers.
///
/// Only the callbacks need static types: WUA calls *into* them, so the vtable layout
/// has to be exact. Everything DrvNest calls *out* to goes through IDispatch with
/// `dynamic`, which needs no GUIDs and cannot get a vtable wrong.
/// </summary>
internal static class WuaConstants
{
    /// <summary>Microsoft Update - the service that actually carries driver offers.</summary>
    public const string MicrosoftUpdateServiceId = "7971f918-a847-4430-9279-4a52d1efe18d";

    // ProgIDs are used instead of CLSIDs so no class GUID has to be hard coded.
    public const string SessionProgId = "Microsoft.Update.Session";
    public const string UpdateCollectionProgId = "Microsoft.Update.UpdateColl";
    public const string ServiceManagerProgId = "Microsoft.Update.ServiceManager";

    // tagServerSelection
    public const int ServerSelectionDefault = 0;
    public const int ServerSelectionManagedServer = 1;
    public const int ServerSelectionWindowsUpdate = 2;
    public const int ServerSelectionOthers = 3;

    // tagDownloadPriority
    public const int DownloadPriorityLow = 1;
    public const int DownloadPriorityNormal = 2;
    public const int DownloadPriorityHigh = 3;
    public const int DownloadPriorityExtraHigh = 4;

    // tagOperationResultCode
    public const int ResultNotStarted = 0;
    public const int ResultInProgress = 1;
    public const int ResultSucceeded = 2;
    public const int ResultSucceededWithErrors = 3;
    public const int ResultFailed = 4;
    public const int ResultAborted = 5;

    // tagAddServiceFlag
    public const int AddServiceAllowPendingRegistration = 1;
    public const int AddServiceAllowOnlineRegistration = 2;
    public const int AddServiceRegisterWithAu = 4;

    // Error codes users actually hit.
    public const int WU_E_OPERATIONINPROGRESS = unchecked((int)0x80240016);
    public const int WU_E_NO_SERVICE = unchecked((int)0x80240043);
    public const int WU_E_LEGACYSERVER = unchecked((int)0x80240019);

    public static bool IsSuccess(int resultCode)
        => resultCode is ResultSucceeded or ResultSucceededWithErrors;

    /// <summary>Turns tagOperationResultCode into something printable.</summary>
    public static string DescribeResultCode(int resultCode) => resultCode switch
    {
        ResultNotStarted => "not started",
        ResultInProgress => "in progress",
        ResultSucceeded => "succeeded",
        ResultSucceededWithErrors => "succeeded with errors",
        ResultFailed => "failed",
        ResultAborted => "aborted",
        _ => $"result code {resultCode}"
    };
}

// =================================================================================================
// Callback interfaces
//
// Signature note: both parameters are declared as `object` with UnmanagedType.Interface.
// That marshals the raw interface pointer into an RCW without needing a static
// declaration of IDownloadJob, IDownloadProgressChangedCallbackArgs and friends - the
// handlers reach into them with `dynamic`. Fewer hand written declarations means fewer
// places to get a vtable wrong.
// =================================================================================================

[ComImport]
[Guid("88AEE058-D4B0-4725-A2F1-814A67AE964C")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISearchCompletedCallback
{
    void Invoke(
        [In, MarshalAs(UnmanagedType.Interface)] object searchJob,
        [In, MarshalAs(UnmanagedType.Interface)] object callbackArgs);
}

[ComImport]
[Guid("8C3F1CDD-6173-4591-AEBD-A56A53CA77C1")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDownloadProgressChangedCallback
{
    void Invoke(
        [In, MarshalAs(UnmanagedType.Interface)] object downloadJob,
        [In, MarshalAs(UnmanagedType.Interface)] object callbackArgs);
}

[ComImport]
[Guid("77254866-9F5B-4C8E-B9E2-C77A8530D64B")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDownloadCompletedCallback
{
    void Invoke(
        [In, MarshalAs(UnmanagedType.Interface)] object downloadJob,
        [In, MarshalAs(UnmanagedType.Interface)] object callbackArgs);
}

[ComImport]
[Guid("E01402D5-F8DA-43BA-A012-38894BD048F1")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IInstallationProgressChangedCallback
{
    void Invoke(
        [In, MarshalAs(UnmanagedType.Interface)] object installationJob,
        [In, MarshalAs(UnmanagedType.Interface)] object callbackArgs);
}

[ComImport]
[Guid("45F4F6F3-D602-4F98-9A8A-3EFA152AD2D3")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IInstallationCompletedCallback
{
    void Invoke(
        [In, MarshalAs(UnmanagedType.Interface)] object installationJob,
        [In, MarshalAs(UnmanagedType.Interface)] object callbackArgs);
}
