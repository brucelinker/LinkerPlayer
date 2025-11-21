using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LinkerPlayer.Interop;

public static class MonitorHelper
{
    private const int MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    public struct MonitorBounds
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [DllImport("User32", SetLastError = true)]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int dwFlags);

    [DllImport("User32", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    [DllImport("User32", SetLastError = true)]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    public static string? GetDeviceName(Window window)
    {
        if (window == null)
        {
            return null;
        }

        try
        {
            WindowInteropHelper helper = new WindowInteropHelper(window);
            IntPtr hwnd = helper.Handle;
            if (hwnd == IntPtr.Zero)
            {
                return null;
            }

            IntPtr hMon = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (hMon == IntPtr.Zero)
            {
                return null;
            }

            MONITORINFOEX info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (GetMonitorInfo(hMon, ref info))
            {
                return info.szDevice; // e.g., "\\\\.\\DISPLAY1"
            }
        }
        catch
        {
            // ignore
        }
        return null;
    }

    public static bool TryGetMonitorBounds(string deviceName, out MonitorBounds bounds)
    {
        bounds = default;
        if (string.IsNullOrWhiteSpace(deviceName))
        {
            return false;
        }

        bool found = false;
        MonitorBounds foundBoundsLocal = default;

        MonitorEnumProc callback = (IntPtr hMon, IntPtr hdc, ref RECT rc, IntPtr data) =>
        {
            MONITORINFOEX info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (GetMonitorInfo(hMon, ref info))
            {
                if (string.Equals(info.szDevice, deviceName, StringComparison.OrdinalIgnoreCase))
                {
                    foundBoundsLocal = new MonitorBounds
                    {
                        Left = rc.left,
                        Top = rc.top,
                        Right = rc.right,
                        Bottom = rc.bottom
                    };
                    found = true;
                    return false; // stop enumeration
                }
            }
            return true; // continue
        };

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);

        if (found)
        {
            bounds = foundBoundsLocal;
            return true;
        }
        return false;
    }
}
