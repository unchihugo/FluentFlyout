using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FluentFlyoutWPF.Classes;

public static class WindowHelper
{
    private const int S_OK = 0;
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int HWND_TOPMOST = -1;
    private const int SWP_NOSIZE = 0x0001;
    private const int SWP_NOMOVE = 0x0002;
    private const int SWP_NOZORDER = 0x0004;
    private const int SWP_SHOWWINDOW = 0x0040;
    private const int SWP_HIDEWINDOW = 0x0080;
    private const int MONITOR_DEFAULTTONEAREST = 2;
    private const int MONITORINFOF_PRIMARY = 1;
    private const int SWP_ASYNCWINDOWPOS = 0x4000;

    public enum MonitorDpiType
    {
        MDT_EFFECTIVE_DPI = 0,
        MDT_ANGULAR_DPI = 1,
        MDT_RAW_DPI = 2,
        MDT_DEFAULT
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;

        public static implicit operator Rect(RECT rect)
        {
            return new Rect(rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct WINDOWPLACEMENT
    {
        public int length;
        public int flags;
        public int showCmd;
        public POINT ptMinPosition;
        public POINT ptMaxPosition;
        public RECT rcNormalPosition;
        public RECT rcDevice;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;

        public static implicit operator MonitorInfo(MONITORINFO other)
        {
            var monitor = new MonitorInfo
            {
                monitorArea = other.rcMonitor,
                workArea = other.rcWork,
                isPrimary = (other.dwFlags & MONITORINFOF_PRIMARY) != 0
            };

            return monitor;
        }
    }

    public struct MonitorInfo
    {
        public Rect monitorArea;
        public Rect workArea;
        public bool isPrimary;
        public uint dpiX;
        public uint dpiY;
    }

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int dwFlags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hMonitor, MonitorDpiType dpiType, out uint dpiX, out uint dpiY);
    [DllImport("user32.dll")]
    private static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, int hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    public static void SetTopmost(Window window) // workaround to set window even more topmost
    {
        var handle = new WindowInteropHelper(window).Handle;
        SetWindowPos(handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
    }

    public static void SetVisibility(Window window, bool visible) // workaround to set window even more topmost
    {
        var handle = new WindowInteropHelper(window).Handle;
        SetWindowPos(handle, 0, 0, 0, 0, 0, (uint)(SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | (visible ? SWP_SHOWWINDOW : SWP_HIDEWINDOW)));
    }

    public static Rect GetPlacement(Window window) // get the window position, ignoring WPF
    {
        WINDOWPLACEMENT wp = new() { length = Marshal.SizeOf<WINDOWPLACEMENT>() };

        var handle = new WindowInteropHelper(window).Handle;
        GetWindowPlacement(handle, ref wp);

        return wp.rcNormalPosition;
    }

    public static void SetPosition(Window window, double x, double y, bool async = false) // set the position of the window, ignoring WPF
    {
        var handle = new WindowInteropHelper(window).Handle;
        SetWindowPos(handle, 0, (int)x, (int)y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | (async ? SWP_ASYNCWINDOWPOS : (uint)0));

        return;
    }

    public static void SetNoActivate(Window window) // prevent window from stealing focus
    {
        window.SourceInitialized += (sender, e) =>
        {
            var helper = new WindowInteropHelper(window);
            SetWindowLong(helper.Handle, GWL_EXSTYLE, GetWindowLong(helper.Handle, GWL_EXSTYLE) | WS_EX_NOACTIVATE);
        };
    }

    private static MonitorInfo getMonitorInfoInternal(IntPtr hMonitor)
    {
        MONITORINFO info = new() { cbSize = Marshal.SizeOf<MONITORINFO>() };

        if (GetMonitorInfo(hMonitor, ref info))
        {
            MonitorInfo new_info = info;

            if (GetDpiForMonitor(hMonitor, MonitorDpiType.MDT_EFFECTIVE_DPI, out uint dpiX, out uint dpiY) == S_OK)
            {
                new_info.dpiX = dpiX;
                new_info.dpiY = dpiY;
            }
            else
            {
                new_info.dpiX = 96;
                new_info.dpiY = 96;
            }

            return new_info;
        }

        return new MonitorInfo();
    }

    public static MonitorInfo GetMonitor(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var hMonitor = MonitorFromWindow(handle, MONITOR_DEFAULTTONEAREST);

        return getMonitorInfoInternal(hMonitor);
    }

    public static IReadOnlyList<MonitorInfo> GetMonitors()
    {
        List<MonitorInfo> result = [];

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
            (hMonitor, _, ref _, _) =>
            {
                result.Add(getMonitorInfoInternal(hMonitor));
                return true;
            },
            IntPtr.Zero
        );

        return result;
    }

}