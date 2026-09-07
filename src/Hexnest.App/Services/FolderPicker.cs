using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Hexnest.Core.Diagnostics;

namespace Hexnest.App.Services;

/// <summary>
/// A folder browse dialog with no extra dependency.
///
/// WPF has no built-in folder picker, and pulling in WindowsAPICodePack or
/// System.Windows.Forms just for this would add megabytes to a single-file
/// executable people download onto a fresh install. SHBrowseForFolder is in
/// shell32.dll, which every Windows machine already has.
/// </summary>
public static class FolderPicker
{
    private const int MAX_PATH = 260;

    private const uint BIF_RETURNONLYFSDIRS = 0x0001;
    private const uint BIF_NEWDIALOGSTYLE = 0x0040;
    private const uint BIF_EDITBOX = 0x0010;

    private const int BFFM_INITIALIZED = 1;
    private const int BFFM_SETSELECTIONW = 0x0467;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct BROWSEINFO
    {
        public IntPtr hwndOwner;
        public IntPtr pidlRoot;
        public IntPtr pszDisplayName;
        public string lpszTitle;
        public uint ulFlags;
        public BrowseCallback lpfn;
        public IntPtr lParam;
        public int iImage;
    }

    private delegate int BrowseCallback(IntPtr hwnd, int msg, IntPtr lParam, IntPtr lpData);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHBrowseForFolderW(ref BROWSEINFO browseInfo);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SHGetPathFromIDListW(IntPtr pidl, StringBuilder path);

    [DllImport("ole32.dll")]
    private static extern void CoTaskMemFree(IntPtr pointer);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageW(IntPtr hwnd, int msg, IntPtr wParam, string lParam);

    /// <summary>Shows the dialog. Returns null when the user cancels.</summary>
    public static string? Pick(string title, string? initialFolder = null)
    {
        IntPtr pidl = IntPtr.Zero;
        IntPtr initialBuffer = IntPtr.Zero;

        try
        {
            if (!string.IsNullOrWhiteSpace(initialFolder) && Directory.Exists(initialFolder))
                initialBuffer = Marshal.StringToCoTaskMemUni(initialFolder);

            // The callback pre-selects the starting folder; without it the dialog
            // always opens at Desktop, which is useless for a rescue USB stick.
            var callback = new BrowseCallback((hwnd, msg, _, lpData) =>
            {
                if (msg == BFFM_INITIALIZED && lpData != IntPtr.Zero)
                    SendMessageW(hwnd, BFFM_SETSELECTIONW, (IntPtr)1, Marshal.PtrToStringUni(lpData) ?? string.Empty);

                return 0;
            });

            var info = new BROWSEINFO
            {
                hwndOwner = GetOwnerHandle(),
                lpszTitle = title,
                ulFlags = BIF_RETURNONLYFSDIRS | BIF_NEWDIALOGSTYLE | BIF_EDITBOX,
                lpfn = callback,
                lParam = initialBuffer
            };

            pidl = SHBrowseForFolderW(ref info);
            if (pidl == IntPtr.Zero) return null;

            var path = new StringBuilder(MAX_PATH);
            return SHGetPathFromIDListW(pidl, path) ? path.ToString() : null;
        }
        catch (Exception ex)
        {
            Log.Error("Folder picker failed", ex);
            return null;
        }
        finally
        {
            if (pidl != IntPtr.Zero) CoTaskMemFree(pidl);
            if (initialBuffer != IntPtr.Zero) Marshal.FreeCoTaskMem(initialBuffer);
        }
    }

    private static IntPtr GetOwnerHandle()
    {
        try
        {
            var window = System.Windows.Application.Current?.MainWindow;
            if (window is null) return IntPtr.Zero;

            return new System.Windows.Interop.WindowInteropHelper(window).Handle;
        }
        catch
        {
            return IntPtr.Zero;
        }
    }
}
