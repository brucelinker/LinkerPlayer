using System.Runtime.InteropServices;

namespace LinkerPlayer.Interop;

internal static class NativeMethods
{
    internal const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int left, top, right, bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MONITORINFO
    {
        public int    cbSize;
        public RECT   rcMonitor;
        public RECT   rcWork;
        public uint   dwFlags;
    }

    [DllImport("user32.dll")]
    internal static extern nint MonitorFromWindow(nint hwnd, uint dwFlags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorInfo(nint hMonitor, ref MONITORINFO lpmi);
}
