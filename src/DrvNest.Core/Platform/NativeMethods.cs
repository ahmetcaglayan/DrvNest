using System.Runtime.InteropServices;
using System.Text;

namespace DrvNest.Core.Platform;

/// <summary>
/// P/Invoke surface for device enumeration.
///
/// Deliberately limited to setupapi.dll, cfgmgr32.dll, kernel32.dll and advapi32.dll:
/// all four ship with every Windows installation, so a freshly formatted machine with
/// nothing installed can still be scanned. No WMI, no COM, no NuGet package.
/// </summary>
internal static class NativeMethods
{
    // ---- SetupDiGetClassDevs flags -------------------------------------------------
    internal const uint DIGCF_DEFAULT = 0x00000001;
    internal const uint DIGCF_PRESENT = 0x00000002;
    internal const uint DIGCF_ALLCLASSES = 0x00000004;
    internal const uint DIGCF_PROFILE = 0x00000008;
    internal const uint DIGCF_DEVICEINTERFACE = 0x00000010;

    // ---- SPDRP_* device registry properties ---------------------------------------
    internal const uint SPDRP_DEVICEDESC = 0x00000000;
    internal const uint SPDRP_HARDWAREID = 0x00000001;
    internal const uint SPDRP_COMPATIBLEIDS = 0x00000002;
    internal const uint SPDRP_SERVICE = 0x00000004;
    internal const uint SPDRP_CLASS = 0x00000007;
    internal const uint SPDRP_CLASSGUID = 0x00000008;
    internal const uint SPDRP_DRIVER = 0x00000009;
    internal const uint SPDRP_CONFIGFLAGS = 0x0000000A;
    internal const uint SPDRP_MFG = 0x0000000B;
    internal const uint SPDRP_FRIENDLYNAME = 0x0000000C;
    internal const uint SPDRP_LOCATION_INFORMATION = 0x0000000D;
    internal const uint SPDRP_INSTALL_STATE = 0x00000022;

    // ---- CM_Get_DevNode_Status ------------------------------------------------------
    internal const uint DN_HAS_PROBLEM = 0x00000400;
    internal const int CR_SUCCESS = 0x00000000;
    internal const int CR_BUFFER_SMALL = 0x0000001A;

    // ---- Common Configuration Manager problem codes --------------------------------
    internal const uint CM_PROB_NOT_CONFIGURED = 1;
    internal const uint CM_PROB_OUT_OF_MEMORY = 3;
    internal const uint CM_PROB_ENTRY_IS_WRONG_TYPE = 4;
    internal const uint CM_PROB_LACKED_ARBITRATOR = 5;
    internal const uint CM_PROB_FAILED_START = 10;
    internal const uint CM_PROB_NORMAL_CONFLICT = 12;
    internal const uint CM_PROB_NEED_RESTART = 14;
    internal const uint CM_PROB_REENUMERATION = 16;
    internal const uint CM_PROB_PARTIAL_LOG_CONF = 17;
    internal const uint CM_PROB_UNKNOWN_RESOURCE = 18;
    internal const uint CM_PROB_REINSTALL = 19;
    internal const uint CM_PROB_DISABLED = 22;
    internal const uint CM_PROB_DEVICE_NOT_THERE = 24;
    internal const uint CM_PROB_FAILED_INSTALL = 28;
    internal const uint CM_PROB_HARDWARE_DISABLED = 29;
    internal const uint CM_PROB_CANT_SHARE_IRQ = 30;
    internal const uint CM_PROB_FAILED_ADD = 31;
    internal const uint CM_PROB_DISABLED_SERVICE = 32;
    internal const uint CM_PROB_TRANSLATION_FAILED = 33;
    internal const uint CM_PROB_NO_SOFTCONFIG = 34;
    internal const uint CM_PROB_DRIVER_FAILED_LOAD = 39;
    internal const uint CM_PROB_DRIVER_BLOCKED = 48;
    internal const uint CM_PROB_FAILED_POST_START = 43;
    internal const uint CM_PROB_HALTED = 44;
    internal const uint CM_PROB_PHANTOM = 45;
    internal const uint CM_PROB_UNSIGNED_DRIVER = 52;

    internal static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    internal struct SP_DEVINFO_DATA
    {
        public uint cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SetupDiGetClassDevsW")]
    internal static extern IntPtr SetupDiGetClassDevs(
        IntPtr classGuid,
        string? enumerator,
        IntPtr hwndParent,
        uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiEnumDeviceInfo(
        IntPtr deviceInfoSet,
        uint memberIndex,
        ref SP_DEVINFO_DATA deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true,
        EntryPoint = "SetupDiGetDeviceRegistryPropertyW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiGetDeviceRegistryProperty(
        IntPtr deviceInfoSet,
        ref SP_DEVINFO_DATA deviceInfoData,
        uint property,
        out uint propertyRegDataType,
        byte[]? propertyBuffer,
        uint propertyBufferSize,
        out uint requiredSize);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true,
        EntryPoint = "SetupDiGetClassDescriptionW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiGetClassDescription(
        ref Guid classGuid,
        StringBuilder classDescription,
        uint classDescriptionSize,
        out uint requiredSize);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, EntryPoint = "CM_Get_Device_IDW")]
    internal static extern int CM_Get_Device_ID(
        uint devInst,
        StringBuilder buffer,
        int bufferLen,
        int flags);

    [DllImport("cfgmgr32.dll", EntryPoint = "CM_Get_Device_ID_Size")]
    internal static extern int CM_Get_Device_ID_Size(out int length, uint devInst, int flags);

    [DllImport("cfgmgr32.dll", EntryPoint = "CM_Get_DevNode_Status")]
    internal static extern int CM_Get_DevNode_Status(
        out uint status,
        out uint problemNumber,
        uint devInst,
        int flags);

    /// <summary>Turns a CM problem code into a short English explanation.</summary>
    internal static string DescribeProblem(uint code) => code switch
    {
        0 => string.Empty,
        CM_PROB_NOT_CONFIGURED => "Device is not configured correctly (no driver bound).",
        CM_PROB_OUT_OF_MEMORY => "Windows could not load the driver: out of memory.",
        CM_PROB_ENTRY_IS_WRONG_TYPE => "The driver registry entry is invalid.",
        CM_PROB_LACKED_ARBITRATOR => "The driver could not obtain the resources it needs.",
        CM_PROB_FAILED_START => "The device cannot start.",
        CM_PROB_NORMAL_CONFLICT => "The device cannot find enough free resources.",
        CM_PROB_NEED_RESTART => "A restart is required to finish configuring this device.",
        CM_PROB_REENUMERATION => "Windows is still enumerating this device.",
        CM_PROB_PARTIAL_LOG_CONF => "Windows cannot determine the device's resources.",
        CM_PROB_UNKNOWN_RESOURCE => "The INF file references an unknown resource type.",
        CM_PROB_REINSTALL => "The driver must be reinstalled.",
        CM_PROB_DISABLED => "The device is disabled.",
        CM_PROB_DEVICE_NOT_THERE => "The device is not present or has been removed.",
        CM_PROB_FAILED_INSTALL => "The drivers for this device are not installed.",
        CM_PROB_HARDWARE_DISABLED => "The device is disabled by firmware.",
        CM_PROB_CANT_SHARE_IRQ => "The device cannot share an interrupt.",
        CM_PROB_FAILED_ADD => "A driver for this device could not be loaded.",
        CM_PROB_DISABLED_SERVICE => "The driver service is disabled.",
        CM_PROB_TRANSLATION_FAILED => "Windows cannot translate the device's resources.",
        CM_PROB_NO_SOFTCONFIG => "No valid configuration for this device.",
        CM_PROB_DRIVER_FAILED_LOAD => "The driver is corrupted or missing.",
        CM_PROB_FAILED_POST_START => "The driver reported a device failure.",
        CM_PROB_HALTED => "The device stopped responding.",
        CM_PROB_PHANTOM => "The device is not connected.",
        CM_PROB_DRIVER_BLOCKED => "The driver is blocked from starting.",
        CM_PROB_UNSIGNED_DRIVER => "The driver's digital signature could not be verified.",
        _ => $"Configuration Manager problem code {code}."
    };
}
