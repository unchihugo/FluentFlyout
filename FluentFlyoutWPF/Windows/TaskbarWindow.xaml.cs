using FluentFlyout.Classes.Settings;
using FluentFlyout.Classes.Utils;
using FluentFlyoutWPF;
using FluentFlyoutWPF.Classes;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace FluentFlyout.Windows;

/// <summary>
/// Interaction logic for TaskbarWindow.xaml
/// </summary>
public partial class TaskbarWindow : Window
{
    // --- Win32 APIs ---
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string? className, string? windowTitle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EnumThreadWindows(uint dwThreadId, EnumWindowsProc enumProc, IntPtr lParam);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetParent(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr lpdwProcessId);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MARGINS
    { public int cxLeftWidth, cxRightWidth, cyTopHeight, cyBottomHeight; }

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hMonitor);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    private const int GWL_STYLE = -16;
    private const int WS_CHILD = 0x40000000;
    private const int WS_POPUP = unchecked((int)0x80000000);

    // SetWindowPos Flags
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint SWP_ASYNCWINDOWPOS = 0x4000;
    // ------------------

    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private readonly DispatcherTimer _timer;
    private readonly SolidColorBrush _hitTestTransparent;
    private readonly int _nativeWidgetsPadding = 216;
    private readonly double _scale = 0.9;

    // unused for now
    //private readonly DoubleAnimation fadeIn = new()
    //{
    //    From = 0.0,
    //    To = 0.5,
    //    Duration = new(TimeSpan.FromSeconds(2)),
    //    FillBehavior = FillBehavior.Stop
    //};
    //private readonly DoubleAnimation fadeOut = new()
    //{
    //    From = 0.5,
    //    To = 0.0,
    //    Duration = new(TimeSpan.FromSeconds(2)),
    //    FillBehavior = FillBehavior.Stop
    //};

    // Cached width calculations
    private string _cachedTitleText = string.Empty;
    private string _cachedArtistText = string.Empty;
    private double _cachedTitleWidth = 0;
    private double _cachedArtistWidth = 0;
    private IntPtr _trayHandle;
    private AutomationElement? _widgetElement;
    private AutomationElement? _trayElement;
    // reference to main window for flyout functions
    private MainWindow? _mainWindow;
    private bool _isPaused;
    private int _recoveryAttempts = 0;
    private int _maxRecoveryAttempts = 5;
    private int _lastSelectedMonitor = -1;
    //private Task _crossFadeTask = Task.CompletedTask;

    public TaskbarWindow()
    {
        WindowHelper.SetNoActivate(this);
        InitializeComponent();
        WindowHelper.SetTopmost(this);

        // Set DataContext for bindings
        DataContext = SettingsManager.Current;

        _hitTestTransparent = new SolidColorBrush(System.Windows.Media.Color.FromArgb(1, 0, 0, 0));

        _timer = new DispatcherTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(1500); // slow auto-update for display changes
        _timer.Tick += (s, e) => UpdatePosition();
        _timer.Start();

        Show();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        HwndSource source = (HwndSource)PresentationSource.FromDependencyObject(this);
        source.AddHook(WindowProc);
    }

    private static IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Some interface mods may collect information from all windows associated with the taskbar,
        // causing the widget and the entire taskbar to freeze.
        // For example, Nilesoft Shell and "Click on empty taskbar space" from Windhawk.
        // Therefore, we are preventing the propagation of this message.
        // Also prevents the widget from blocking taskbar's message processing, which is another source of freezes.
        switch (msg)
        {
            case 0x003D: // WM_GETOBJECT (Sent by Microsoft UI Automation to obtain information about an accessible object contained in a server application)
            case 0x0018: // WM_SHOWWINDOW
            case 0x0046: // WM_WINDOWPOSCHANGING - Triggers during alt-tabs, window changes
            case 0x0083: // WM_NCCALCSIZE - Can trigger layout storms
            case 0x0281: // WM_IME_SETCONTEXT - IME conflicts
            case 0x0282: // WM_IME_NOTIFY
                handled = true;
                return IntPtr.Zero;

                // Handle other known harmless messages that are sent when FluentFlyout starts, Windows locks, etc.
                // Needs testing
                //case 0x0047:
                //case 0x02B1:
                //case 0x001E:
                //case 0x0164:
                //case 0xC25F:
                //    handled = true;
                //    return IntPtr.Zero;
        }

        return IntPtr.Zero;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        SetupWindow();
        _mainWindow = (MainWindow)Application.Current.MainWindow;
    }

    //private void Grid_MouseEnter(object sender, MouseEventArgs e)
    //{
    //    // hover effects
    //    var brush = (SolidColorBrush)Application.Current.Resources["TextFillColorSecondaryBrush"];
    //    MainBorder.Background = new SolidColorBrush(brush.Color) { Opacity = 0.075 };
    //    var secondBrush = (SolidColorBrush)Application.Current.Resources["TextFillColorDisabledBrush"];
    //    TopBorder.BorderBrush = new SolidColorBrush(secondBrush.Color) { Opacity = 0.2 };
    //}

    //private void Grid_MouseLeave(object sender, MouseEventArgs e)
    //{
    //    MainBorder.Background = Brushes.Transparent;
    //    TopBorder.BorderBrush = Brushes.Transparent;
    //}

    private void Grid_MouseEnter(object sender, MouseEventArgs e)
    {
        if (!SettingsManager.Current.TaskbarWidgetClickable || String.IsNullOrEmpty(SongTitle.Text + SongArtist.Text)) return;

        SolidColorBrush targetBackgroundBrush;
        // hover effects with animations, hard-coded colors because I can't find the resource brushes
        if (ApplicationThemeManager.GetSystemTheme() == SystemTheme.Dark)
        { // dark mode
            targetBackgroundBrush = new SolidColorBrush(Color.FromArgb(197, 255, 255, 255)) { Opacity = 0.075 };
            TopBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(93, 255, 255, 255)) { Opacity = 0.25 };
        }
        else
        { // light mode
            targetBackgroundBrush = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255)) { Opacity = 0.6 };
            TopBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(93, 255, 255, 255)) { Opacity = 1 };
        }

        // Animate background
        var backgroundAnimation = new ColorAnimation
        {
            To = targetBackgroundBrush.Color,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        var backgroundOpacityAnimation = new DoubleAnimation
        {
            To = targetBackgroundBrush.Opacity,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        // rare case where background is not a SolidColorBrush after SetupWindow
        if (MainBorder.Background is not SolidColorBrush)
        {
            MainBorder.Background = new SolidColorBrush(Colors.Transparent);
            MainBorder.Background.Opacity = 0;
        }

        MainBorder.Background.BeginAnimation(SolidColorBrush.ColorProperty, backgroundAnimation);
        MainBorder.Background.BeginAnimation(SolidColorBrush.OpacityProperty, backgroundOpacityAnimation);
    }

    private void Grid_MouseLeave(object sender, MouseEventArgs e)
    {
        if (!SettingsManager.Current.TaskbarWidgetClickable || String.IsNullOrEmpty(SongTitle.Text + SongArtist.Text)) return;

        // Animate back to transparent
        var backgroundAnimation = new ColorAnimation
        {
            To = Colors.Transparent,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };

        var backgroundOpacityAnimation = new DoubleAnimation
        {
            To = 0,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };

        MainBorder.Background?.BeginAnimation(SolidColorBrush.ColorProperty, backgroundAnimation);
        MainBorder.Background?.BeginAnimation(SolidColorBrush.OpacityProperty, backgroundOpacityAnimation);

        TopBorder.BorderBrush = System.Windows.Media.Brushes.Transparent;
    }

    private void Grid_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!SettingsManager.Current.TaskbarWidgetClickable || _mainWindow == null) return;

        // flyout main flyout when clicked
        _mainWindow.ShowMediaFlyout();
    }

    private IntPtr GetSelectedTaskbarHandle(out bool isMainTaskbarSelected)
    {
        var monitors = WindowHelper.GetMonitors();
        var selectedMonitor = monitors[Math.Clamp(SettingsManager.Current.TaskbarWidgetSelectedMonitor, 0, monitors.Count - 1)];
        isMainTaskbarSelected = true;

        // Get the main taskbar and check if it is on the selected monitor.
        var mainHwnd = FindWindow("Shell_TrayWnd", null);
        if (WindowHelper.GetMonitor(mainHwnd).deviceId == selectedMonitor.deviceId)
            return mainHwnd;

        if (monitors.Count == 1)
            return mainHwnd;

        isMainTaskbarSelected = false;
        if (monitors.Count == 2)
        {
            var hwnd = FindWindow("Shell_SecondaryTrayWnd", null);
            if (WindowHelper.GetMonitor(hwnd).deviceId == selectedMonitor.deviceId)
            {
                return hwnd;
            }
            else
            {
                isMainTaskbarSelected = true;
                return mainHwnd;
            }
        }

        // If there are more than two monitors, we will need to enumerate all existing windows
        // to find all Shell_SecondaryTrayWnd among them.

        IntPtr secondHwnd = IntPtr.Zero;
        StringBuilder className = new(256); // 256 is the maximum class name length
        IntPtr checkWindowClass(IntPtr wnd)
        {
            var len = GetClassName(wnd, className, className.Capacity);
            if (className.Equals("Shell_SecondaryTrayWnd"))
            {
                if (WindowHelper.GetMonitor(wnd).deviceId == selectedMonitor.deviceId)
                {
                    return wnd;
                }
            }
            return IntPtr.Zero;
        }

        // Get the threadId of the main taskbar and check all windows created in the same thread.
        // This is very fast, but in some cases Shell_TrayWnd and other Shell_SecondaryTrayWnd's may be created in different threads.
        // Actually, I couldn't achieve that kind of behavior.
        if (mainHwnd != IntPtr.Zero)
        {
            uint threadId = GetWindowThreadProcessId(mainHwnd, IntPtr.Zero);
            EnumThreadWindows(threadId, (wnd, param) =>
            {
                secondHwnd = checkWindowClass(wnd);
                if (secondHwnd != IntPtr.Zero)
                    return false; // stop

                return true;
            }, IntPtr.Zero);

            if (secondHwnd != IntPtr.Zero)
                return secondHwnd;
        }

        // If for some reason the taskbars were created in different threads or simply could not be found,
        // we try to find them among all existing windows.
        EnumWindows((wnd, param) =>
        {
            secondHwnd = checkWindowClass(wnd);
            if (secondHwnd != IntPtr.Zero)
                return false; // stop

            return true;
        }, IntPtr.Zero);

        if (secondHwnd != IntPtr.Zero)
            return secondHwnd;

        // Logger.Debug($"No taskbar found on the selected monitor. Using the main taskbar.");
        isMainTaskbarSelected = true;
        return mainHwnd;
    }

    private void SetupWindow()
    {
        try
        {
            var interop = new WindowInteropHelper(this);
            IntPtr myHandle = interop.Handle;

            Background = _hitTestTransparent; // ensures that non-content areas also trigger MouseEnter event

            IntPtr taskbarHandle = GetSelectedTaskbarHandle(out bool isMainTaskbarSelected);

            // This prevents the window from trying to float above the taskbar as a separate entity
            int style = GetWindowLong(myHandle, GWL_STYLE);
            style = (style & ~WS_POPUP) | WS_CHILD;
            SetWindowLong(myHandle, GWL_STYLE, style);

            SetParent(myHandle, taskbarHandle); // if this window is created faster than the Taskbar is loaded, then taskbarHandle will be NULL.

            CalculateAndSetPosition(taskbarHandle, myHandle, isMainTaskbarSelected);

            // for hover animation
            if (MainBorder.Background is not SolidColorBrush)
            {
                MainBorder.Background = new SolidColorBrush(Colors.Transparent);
                MainBorder.Background.Opacity = 0;
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Taskbar Widget error during setup");
        }
    }

    private void UpdatePosition()
    {
        // Check premium status before allowing widget to be displayed
        if (!SettingsManager.Current.TaskbarWidgetEnabled || !SettingsManager.Current.IsPremiumUnlocked)
            return;

        try
        {
            var interop = new WindowInteropHelper(this);
            IntPtr taskbarHandle = GetSelectedTaskbarHandle(out bool isMainTaskbarSelected);

            if (interop.Handle == IntPtr.Zero) // window handle lost, try to reset
            {
                _timer.Stop();

                if (_recoveryAttempts >= _maxRecoveryAttempts)
                {
                    Logger.Warn("Taskbar Widget window handle is zero and recovery already attempted, stopping updates.");
                    return; // already tried recovery, don't loop
                }

                Logger.Warn("Taskbar Widget window handle is zero, attempting recovery...");

                Dispatcher.BeginInvoke(async () =>
                {
                    await Task.Delay(1000); // delay before recovery to let taskbar stabilize
                    try
                    {
                        _mainWindow?.RecreateTaskbarWindow();
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "Failed to signal MainWindow to recover Taskbar Widget window");
                        _recoveryAttempts++;
                    }
                }, DispatcherPriority.Background);

                return;
            }

            // If the Taskbar was not found during initialization or another taskbar was selected,
            // then we need to set the Taskbar as the Parent here.
            if (GetParent(interop.Handle) != taskbarHandle)
            {
                SetParent(interop.Handle, taskbarHandle);
            }

            if (taskbarHandle != IntPtr.Zero && interop.Handle != IntPtr.Zero)
            {
                Dispatcher.BeginInvoke(() =>
                {
                    CalculateAndSetPosition(taskbarHandle, interop.Handle, isMainTaskbarSelected);
                }, DispatcherPriority.Background);
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Taskbar Widget error during position update");
        }
    }

    private void CalculateAndSetPosition(IntPtr taskbarHandle, IntPtr myHandle, bool isMainTaskbarSelected)
    {
        // get DPI scaling
        double dpiScale = GetDpiForWindow(taskbarHandle) / 96.0;

        // calculate widget width - use cached values if text hasn't changed
        string currentTitle = SongTitle.Text;
        string currentArtist = SongArtist.Text;

        if (!string.Equals(currentTitle, _cachedTitleText, StringComparison.Ordinal))
        {
            _cachedTitleWidth = StringWidth.GetStringWidth(currentTitle, 400);
            _cachedTitleText = currentTitle;
        }
        if (!string.Equals(currentArtist, _cachedArtistText, StringComparison.Ordinal))
        {
            _cachedArtistWidth = StringWidth.GetStringWidth(currentArtist, 400);
            _cachedArtistText = currentArtist;
        }

        double logicalWidth = Math.Max(_cachedTitleWidth, _cachedArtistWidth) + 55; // add margin for cover image
        // maximum width limit, same as Windows native widget
        logicalWidth = Math.Min(logicalWidth, _nativeWidgetsPadding / _scale);

        SongTitle.Width = Math.Max(logicalWidth - 58, 0);
        SongArtist.Width = Math.Max(logicalWidth - 58, 0);

        // add space for playback controls if enabled and visible
        if (SettingsManager.Current.TaskbarWidgetControlsEnabled && ControlsStackPanel.Visibility == Visibility.Visible)
        {
            logicalWidth += (int)(102);
        }

        int physicalWidth = (int)(logicalWidth * dpiScale * _scale);
        int physicalHeight = (int)(40 * dpiScale); // default height

        // Get Taskbar dimensions
        RECT taskbarRect;
        DwmGetWindowAttribute(taskbarHandle, DWMWA_EXTENDED_FRAME_BOUNDS, out taskbarRect, Marshal.SizeOf(typeof(RECT)));
        int taskbarHeight = taskbarRect.Bottom - taskbarRect.Top;

        // Centered vertically
        int physicalTop = taskbarRect.Top + (taskbarHeight - physicalHeight) / 2;

        int physicalLeft = taskbarRect.Left;
        switch (SettingsManager.Current.TaskbarWidgetPosition)
        {
            case 0: // left aligned with some padding (like native widgets)
                physicalLeft += 20;
                if (!SettingsManager.Current.TaskbarWidgetPadding)
                    break;

                // automatic widget padding to the left
                try
                {
                    // find widget button in XAML
                    (bool found, Rect widgetRect) = GetTaskbarWidgetRect(taskbarHandle);

                    // make sure it's on the left side, otherwise ignore (widget might be to the right)
                    if (found && widgetRect.Right < (taskbarRect.Left + taskbarRect.Right) / 2)
                        physicalLeft = (int)(widgetRect.Right) + 2; // add small padding
                }
                catch (Exception ex)
                {
                    // fallback to default padding
                    Logger.Warn(ex, "Failed to get Widgets button position.");
                    physicalLeft += _nativeWidgetsPadding + 2;
                }
                break;

            case 1: // center of the taskbar
                physicalLeft += (taskbarRect.Right - taskbarRect.Left - physicalWidth) / 2;
                break;

            case 2: // right aligned next to system tray with tiny bit of padding
                try
                {
                    // try to position next to widgets button if enabled
                    if (SettingsManager.Current.TaskbarWidgetPadding)
                    {
                        try
                        {
                            // find widget button in XAML
                            (bool found, Rect widgetRect) = GetTaskbarWidgetRect(taskbarHandle);

                            // make sure it's on the right side, otherwise ignore (widget might be to the left)
                            if (found && widgetRect.Left > (taskbarRect.Left + taskbarRect.Right) / 2)
                            {
                                physicalLeft = (int)(widgetRect.Left) - 1 - physicalWidth; // left of widget
                                break; // early exit so we don't move it back next to tray below
                            }
                        }
                        catch (Exception ex) // catch exception when getting widget position
                        {
                            Logger.Warn(ex, "Failed to get Widgets button position.");
                        }
                    }

                    // try to position next to system tray
                    if (!isMainTaskbarSelected)
                    {
                        // find secondary tray with automation
                        (bool found, Rect secondaryTrayRect) = GetSystemTrayRect(taskbarHandle);

                        if (found)
                        {
                            physicalLeft = (int)secondaryTrayRect.Left - physicalWidth - 1;
                            break;
                        }
                    }
                    else if (_trayHandle == IntPtr.Zero || _lastSelectedMonitor != SettingsManager.Current.TaskbarWidgetSelectedMonitor)
                    {
                        if (isMainTaskbarSelected)
                        {
                            // find primary tray handle
                            _trayHandle = FindWindowEx(taskbarHandle, IntPtr.Zero, "TrayNotifyWnd", null);
                        }
                    }

                    // the code reaches here because:
                    // primary taskbar monitor is selected and auto widget padding setting is off

                    // if the tray handle is zero, fallback to right alignment,
                    // since we are aligning to the right side and know the size of the taskbar.
                    if (_trayHandle == IntPtr.Zero)
                    {
                        physicalLeft = taskbarRect.Right - physicalWidth - 20;
                        break;
                    }
                    GetWindowRect(_trayHandle, out RECT trayRect);
                    physicalLeft = trayRect.Left - physicalWidth - 1;
                }
                catch (Exception ex)
                {
                    // Fallback to left alignment
                    Logger.Warn(ex, "Failed to get System Tray position.");
                    physicalLeft = taskbarRect.Left + 20;
                }
                break;
        }

        // TODO: Finish: Update visibility to force layout update after DPI/monitor change
        //if (SongInfoStackPanel.Visibility == Visibility.Visible)
        //{
        //    SongInfoStackPanel.Visibility = Visibility.Collapsed;
        //    SongInfoStackPanel.Visibility = Visibility.Visible;
        //}

        physicalLeft += SettingsManager.Current.TaskbarWidgetManualPadding;

        // Following SetWindowPos will set the position relative to the parent window,
        // so those coordinates need to be converted.
        POINT relativePos = new() { X = physicalLeft, Y = physicalTop };
        ScreenToClient(taskbarHandle, ref relativePos);

        // Apply using SetWindowPos (Bypassing WPF layout engine)
        SetWindowPos(myHandle, IntPtr.Zero,
                 relativePos.X, relativePos.Y,
                 physicalWidth, physicalHeight,
                 SWP_NOZORDER | SWP_NOACTIVATE | SWP_ASYNCWINDOWPOS | SWP_SHOWWINDOW);

        _lastSelectedMonitor = SettingsManager.Current.TaskbarWidgetSelectedMonitor;
    }

    public void UpdateUi(string title, string artist, BitmapImage? icon, GlobalSystemMediaTransportControlsSessionPlaybackStatus? playbackStatus, GlobalSystemMediaTransportControlsSessionPlaybackControls? playbackControls = null)
    {
        // Check premium status - hide widget if not unlocked
        if ((!SettingsManager.Current.TaskbarWidgetEnabled || !SettingsManager.Current.IsPremiumUnlocked))
        {
            if (_timer.IsEnabled) // pause timer to save resources
                _timer.Stop();

            Dispatcher.Invoke(() =>
            {
                Visibility = Visibility.Collapsed;
            });
            return;
        }

        if (!_timer.IsEnabled)
            _timer.Start();

        if (title == "-" && artist == "-")
        {
            // no media playing, hide UI
            Dispatcher.Invoke(() =>
            {
                if (SettingsManager.Current.TaskbarWidgetHideCompletely)
                {
                    Visibility = Visibility.Collapsed;
                    return;
                }

                ControlsStackPanel.Visibility = Visibility.Collapsed;
                SongTitle.Text = string.Empty;
                SongArtist.Text = string.Empty;
                SongInfoStackPanel.Visibility = Visibility.Collapsed;
                SongInfoStackPanel.ToolTip = string.Empty;
                SongImagePlaceholder.Symbol = SymbolRegular.MusicNote220;
                SongImagePlaceholder.Visibility = Visibility.Visible;
                SongImage.ImageSource = null;
                BackgroundImage.Source = null;
                SongImageBorder.Margin = new Thickness(0, 0, 0, -3); // align music note better when no cover

                MainBorder.Background = new SolidColorBrush(Colors.Transparent);
                MainBorder.Background.Opacity = 0;
                TopBorder.BorderBrush = Brushes.Transparent;

                UpdatePosition();
                Visibility = Visibility.Visible;
            });
            return;
        }

        _isPaused = false;
        if (playbackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
        {
            _isPaused = true;
        }

        // adjust UI based on available controls
        Dispatcher.Invoke(() =>
        {
            if (SettingsManager.Current.TaskbarWidgetControlsEnabled && playbackControls != null)
            {
                PreviousButton.IsHitTestVisible = playbackControls.IsPreviousEnabled;
                PlayPauseButton.IsHitTestVisible = playbackControls.IsPauseEnabled || playbackControls.IsPlayEnabled;
                NextButton.IsHitTestVisible = playbackControls.IsNextEnabled;

                PreviousButton.Opacity = playbackControls.IsPreviousEnabled ? 1 : 0.5;
                PlayPauseButton.Opacity = (playbackControls.IsPauseEnabled || playbackControls.IsPlayEnabled) ? 1 : 0.5;
                NextButton.Opacity = playbackControls.IsNextEnabled ? 1 : 0.5;
            }
            else
            {
                PreviousButton.IsHitTestVisible = false;
                PlayPauseButton.IsHitTestVisible = false;
                NextButton.IsHitTestVisible = false;

                PreviousButton.Opacity = 0.5;
                NextButton.Opacity = 0.5;
                PlayPauseButton.Opacity = 0.5;
            }
        });

        Dispatcher.Invoke(() =>
        {
            if (SongTitle.Text != title && SongArtist.Text != artist)
            {
                // changed info
                if (SettingsManager.Current.TaskbarWidgetAnimated)
                {
                    AnimateEntrance();
                }
            }

            SongTitle.Text = !String.IsNullOrEmpty(title) ? title : "-";
            SongArtist.Text = !String.IsNullOrEmpty(artist) ? artist : "-";

            // Update tooltip with song info
            SongInfoStackPanel.ToolTip = string.Empty;
            SongInfoStackPanel.ToolTip += !String.IsNullOrEmpty(title) ? title : string.Empty;
            SongInfoStackPanel.ToolTip += !String.IsNullOrEmpty(artist) ? "\n\n" + artist : string.Empty;

            if (SettingsManager.Current.TaskbarWidgetControlsEnabled)
            {
                PlayPauseButton.Icon = _isPaused ? new SymbolIcon(SymbolRegular.Play24, filled: true) : new SymbolIcon(SymbolRegular.Pause24, filled: true);
            }

            if (icon != null)
            {
                if (_isPaused)
                { // show pause icon overlay
                    SongImagePlaceholder.Symbol = SymbolRegular.Pause24;
                    SongImagePlaceholder.Visibility = Visibility.Visible;
                    SongImage.Opacity = 0.4;
                }
                else
                {
                    SongImagePlaceholder.Visibility = Visibility.Collapsed;
                    SongImage.Opacity = 1;
                }
                SongImage.ImageSource = icon;
                BackgroundImage.Source = icon;
                SongImageBorder.Margin = new Thickness(0, 0, 0, -2); // align image better when cover is present

                // start cross-fade if previous task is completed
                //if (_crossFadeTask.IsCompleted)
                //{
                //    _crossFadeTask = CrossFadeBackground(icon);
                //}
            }
            else
            {
                SongImagePlaceholder.Symbol = SymbolRegular.MusicNote220;
                SongImagePlaceholder.Visibility = Visibility.Visible;
                SongImage.ImageSource = null;
                BackgroundImage.Source = null;
            }

            SongTitle.Visibility = Visibility.Visible;
            SongArtist.Visibility = !String.IsNullOrEmpty(artist) ? Visibility.Visible : Visibility.Collapsed; // hide artist if it's not available
            SongInfoStackPanel.Visibility = Visibility.Visible;
            BackgroundImage.Visibility = SettingsManager.Current.TaskbarWidgetBackgroundBlur ? Visibility.Visible : Visibility.Collapsed;

            // on top of XAML visibility binding (XAML binding only hides when disabled in settings)
            if (SettingsManager.Current.TaskbarWidgetControlsEnabled)
            {
                ControlsStackPanel.Visibility = Visibility.Visible;
            }

            Visibility = Visibility.Visible;

            // defer UpdatePosition to allow WPF layout to complete first
            Dispatcher.BeginInvoke(() => UpdatePosition(), DispatcherPriority.Background);
        });
    }

    private async void AnimateEntrance()
    {
        try
        {
            int msDuration = _mainWindow != null ? _mainWindow.getDuration() : 300;

            // opacity and left to right animation for SongInfoStackPanel
            DoubleAnimation opacityAnimation = new()
            {
                From = 0.0,
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(msDuration),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            DoubleAnimation translateAnimation = new()
            {
                From = -10,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(msDuration),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            // Apply animations
            SongInfoStackPanel.BeginAnimation(OpacityProperty, opacityAnimation);
            TranslateTransform translateTransform = new();
            SongInfoStackPanel.RenderTransform = translateTransform;
            translateTransform.BeginAnimation(TranslateTransform.XProperty, translateAnimation);

            // don't play ControlsStackPanel animation if it's not enabled
            if (!SettingsManager.Current.TaskbarWidgetControlsEnabled)
                return;

            ControlsStackPanel.BeginAnimation(OpacityProperty, opacityAnimation);
            TranslateTransform translateTransform2 = new();
            ControlsStackPanel.RenderTransform = translateTransform2;
            translateTransform2.BeginAnimation(TranslateTransform.XProperty, translateAnimation);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Taskbar Widget error during entrance animation");
        }
    }

    //private Task CrossFadeBackground(BitmapImage newImage)
    //{
    //    try
    //    {
    //        BackgroundImageNext.Source = newImage;

    //        fadeIn.Completed += (s, e) =>
    //        {
    //            BackgroundImage.Source = newImage;
    //            BackgroundImageNext.Opacity = 0;
    //            BackgroundImageNext.Source = null;
    //            fadeIn.Completed -= (s, e2) => {  };
    //        };

    //        BackgroundImage.BeginAnimation(OpacityProperty, fadeOut);
    //        BackgroundImageNext.BeginAnimation(OpacityProperty, fadeIn);
    //        return Task.CompletedTask;
    //    }
    //    catch
    //    {
    //        // ignore errors
    //        return Task.CompletedTask;
    //    }
    //}

    private (bool, Rect) GetTaskbarXamlElementRect(IntPtr taskbarHandle, ref AutomationElement? elementCache, string elementName)
    {
        try
        {
            // reset if monitor changed
            if (_lastSelectedMonitor != SettingsManager.Current.TaskbarWidgetSelectedMonitor)
                elementCache = null;

            // find widget in XAML
            if (elementCache == null)
            {
                AutomationElement root = AutomationElement.FromHandle(taskbarHandle);

                elementCache = root.FindFirst(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.AutomationIdProperty, elementName));
            }

            if (elementCache == null) // widget most likely disabled
                return (false, Rect.Empty);

            try
            {
                Rect elementRect = elementCache.Current.BoundingRectangle;

                if (elementRect == Rect.Empty) // widget shown before but most likely disabled now
                {
                    elementCache = null; // reset cache
                    return (false, Rect.Empty);
                }

                return (true, elementRect);
            }
            catch (ElementNotAvailableException)
            {
                // element became stale, reset cache
                Logger.Warn("Taskbar XAML element became stale, resetting cache: " + elementName);
                elementCache = null;
                return (false, Rect.Empty);
            }
        }
        catch (COMException ex)
        {
            Logger.Error(ex, "COM error retrieving taskbar XAML element Rect: " + elementName);
            elementCache = null; // reset cache on error
            return (false, Rect.Empty);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error retrieving taskbar XAML element Rect: " + elementName);
            elementCache = null; // reset cache on error
            return (false, Rect.Empty);
        }
    }

    /// <summary>
    /// Attempts to locate the Windows taskbar widgets button and retrieves its bounding rectangle.
    /// </summary>
    /// <returns>A tuple where the first value indicates whether the widgets button was found (<see langword="true"/> if found;
    /// otherwise, <see langword="false"/>), and the second value is the bounding rectangle of the button if found, or
    /// <see cref="Rect.Empty"/> if not found.</returns>
    private (bool, Rect) GetTaskbarWidgetRect(IntPtr taskbarHandle)
    {
        return GetTaskbarXamlElementRect(taskbarHandle, ref _widgetElement, "WidgetsButton");
    }

    private (bool, Rect) GetSystemTrayRect(IntPtr taskbarHandle)
    {
        return GetTaskbarXamlElementRect(taskbarHandle, ref _trayElement, "SystemTrayIcon");
    }

    // event handlers for media control buttons
    private async void Previous_Click(object sender, RoutedEventArgs e)
    {
        if (_mainWindow == null) return;

        var mediaManager = _mainWindow.mediaManager;
        if (mediaManager == null) return;

        var focusedSession = mediaManager.GetFocusedSession();
        if (focusedSession == null) return;

        await focusedSession.ControlSession.TrySkipPreviousAsync();
    }

    private async void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (_mainWindow == null) return;

        var mediaManager = _mainWindow.mediaManager;
        if (mediaManager == null) return;

        var focusedSession = mediaManager.GetFocusedSession();
        if (focusedSession == null) return;

        if (_isPaused) // paused
        {
            await focusedSession.ControlSession.TryPlayAsync();
        }
        else // playing
        {
            await focusedSession.ControlSession.TryPauseAsync();
        }
    }

    private async void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_mainWindow == null) return;

        var mediaManager = _mainWindow.mediaManager;
        if (mediaManager == null) return;

        var focusedSession = mediaManager.GetFocusedSession();
        if (focusedSession == null) return;

        await focusedSession.ControlSession.TrySkipNextAsync();
    }
}