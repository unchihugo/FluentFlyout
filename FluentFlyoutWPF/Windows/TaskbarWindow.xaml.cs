using FluentFlyout.Classes.Utils;
using FluentFlyoutWPF;
using FluentFlyoutWPF.Classes;
using System.Diagnostics.Eventing.Reader;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Wpf.Ui.Markup;

namespace FluentFlyout.Windows;

/// <summary>
/// Interaction logic for TaskbarWindow.xaml
/// </summary>
public partial class TaskbarWindow : Window
{
    // --- Win32 APIs ---
    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string className, string windowTitle);

    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("dwmapi.dll")]
    static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MARGINS { public int cxLeftWidth, cxRightWidth, cyTopHeight, cyBottomHeight; }

    [DllImport("user32.dll")]
    static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    private const int GWL_STYLE = -16;
    private const int WS_CHILD = 0x40000000;
    private const int WS_POPUP = unchecked((int)0x80000000);

    // SetWindowPos Flags
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint SWP_ASYNCWINDOWPOS = 0x4000;
    // ------------------

    private DispatcherTimer _timer;
    private double _left, _top;

    SolidColorBrush _hitTestTransparent;


    public TaskbarWindow()
    {
        WindowHelper.SetNoActivate(this);
        InitializeComponent();
        WindowHelper.SetTopmost(this);

        _hitTestTransparent = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));

        _timer = new DispatcherTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(50);
        _timer.Tick += (s, e) => UpdatePosition();
        _timer.Start();

        Show();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        SetupWindow();
    }

    private void Grid_MouseEnter(object sender, MouseEventArgs e)
    {
        // hover effects
        var brush = (SolidColorBrush)Application.Current.Resources["TextFillColorSecondaryBrush"];
        MainBorder.Background = new SolidColorBrush(brush.Color) { Opacity = 0.075 };
        var secondBrush = (SolidColorBrush)Application.Current.Resources["TextFillColorDisabledBrush"];
        TopBorder.BorderBrush = new SolidColorBrush(secondBrush.Color) { Opacity = 0.2 };
    }

    private void Grid_MouseLeave(object sender, MouseEventArgs e)
    {
        MainBorder.Background = Brushes.Transparent;
        TopBorder.BorderBrush = Brushes.Transparent;
    }

    private void Grid_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // flyout main flyout when clicked
        var mainWindow = (MainWindow)Application.Current.MainWindow;
        mainWindow.ShowMediaFlyout();
    }

    private void SetupWindow()
    {
        var interop = new WindowInteropHelper(this);
        IntPtr myHandle = interop.Handle;

        // 2. Enable DWM Transparency (The "Glass" Trick)
        // This removes the black background and makes it transparent 
        //MARGINS margins = new MARGINS { cxLeftWidth = -1 }; // -1 extends glass to full window
        //DwmExtendFrameIntoClientArea(myHandle, ref margins);
        Background = _hitTestTransparent; // ensures that non-content areas also trigger MouseEnter event

        // 3. Find the Taskbar and ReBar
        IntPtr taskbarHandle = FindWindow("Shell_TrayWnd", null);
        IntPtr rebarHandle = FindWindowEx(taskbarHandle, IntPtr.Zero, "ReBarWindow32", null);

        if (taskbarHandle != IntPtr.Zero)
        {
            // 1. Modify Style: Remove Popup, Add Child
            // This prevents the window from trying to float above the taskbar as a separate entity
            int style = GetWindowLong(myHandle, GWL_STYLE);
            style = (style & ~WS_POPUP) | WS_CHILD;
            SetWindowLong(myHandle, GWL_STYLE, style);

            // 2. Set Parent
            SetParent(myHandle, taskbarHandle);

            // 3. Initial Calculation
            CalculateAndSetPosition(taskbarHandle, myHandle);
        }
    }

        private void UpdatePosition()
    {
        var interop = new WindowInteropHelper(this);
        IntPtr taskbarHandle = FindWindow("Shell_TrayWnd", null);

        if (taskbarHandle != IntPtr.Zero && interop.Handle != IntPtr.Zero)
        {
            CalculateAndSetPosition(taskbarHandle, interop.Handle);
        }
    }

    private void CalculateAndSetPosition(IntPtr taskbarHandle, IntPtr myHandle)
    {
        // 1. Get scaling factor (DPI)
        PresentationSource source = PresentationSource.FromVisual(this);
        double dpiScaleX = source.CompositionTarget.TransformToDevice.M11;
        double dpiScaleY = source.CompositionTarget.TransformToDevice.M22;

        var scale = 0.9;
        // 2. Calculate desired width based on text
        // Note: Assuming GetStringWidth returns logical pixels. 
        var titleWidth = StringWidth.GetStringWidth(SongTitle.Text);
        var artistWidth = StringWidth.GetStringWidth(SongArtist.Text);
        double logicalWidth = Math.Max(titleWidth, artistWidth) + 46 * scale; // add margin for cover image

        // Convert logical width to physical pixels for SetWindowPos
        int physicalWidth = (int)(logicalWidth * dpiScaleX);
        int physicalHeight = (int)(this.Height * dpiScaleY);

        // 3. Get Taskbar dimensions
        RECT taskbarRect;
        GetWindowRect(taskbarHandle, out taskbarRect);
        int taskbarHeight = taskbarRect.Bottom - taskbarRect.Top;

        // 4. Calculate Top/Left in PHYSICAL PIXELS relative to Taskbar
        // Centered vertically
        int physicalTop = (taskbarHeight - physicalHeight) / 2;
        int physicalLeft = 10; // Or your custom logic

        // 5. Apply using SetWindowPos (Bypassing WPF layout engine)
        SetWindowPos(myHandle, IntPtr.Zero,
                     physicalLeft, physicalTop,
                     physicalWidth, physicalHeight,
                     SWP_NOZORDER | SWP_NOACTIVATE | SWP_ASYNCWINDOWPOS | SWP_SHOWWINDOW);
    }


    public void UpdateUi(string title, string artist, BitmapImage? icon)
    {
        Dispatcher.Invoke(() =>
        {
            if (!String.IsNullOrEmpty(title))
            {
                SongTitle.Text = title;
            }
            else
            {
                SongTitle.Text = "-";
            }

            if (!String.IsNullOrEmpty(artist))
            {
                SongArtist.Text = artist;
            }
            else
            {
                SongArtist.Text = "-";
            }

            if (icon != null)
            {
                SongImagePlaceholder.Visibility = Visibility.Collapsed;
                SongImage.ImageSource = icon;
            }
            else
            {
                SongImagePlaceholder.Visibility = Visibility.Visible;
                SongImage.ImageSource = null;
            }
        });
    }
}
