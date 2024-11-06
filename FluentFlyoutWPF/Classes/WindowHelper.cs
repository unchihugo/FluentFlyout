using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FluentFlyout.Classes;

class WindowHelper
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int HWND_TOPMOST = -1;
    private const int SWP_NOSIZE = 0x0001;
    private const int SWP_NOMOVE = 0x0002;
    private const int SWP_SHOWWINDOW = 0x0040;

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

    public static void SetNoActivate(Window window) // prevent window from stealing focus
    {
        window.SourceInitialized += (sender, e) =>
        {
            var helper = new WindowInteropHelper(window);
            SetWindowLong(helper.Handle, GWL_EXSTYLE, GetWindowLong(helper.Handle, GWL_EXSTYLE) | WS_EX_NOACTIVATE);
        };
    }
}