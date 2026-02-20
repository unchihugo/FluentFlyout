// Copyright © 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF;
using FluentFlyoutWPF.Classes;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;
using static FluentFlyout.Classes.NativeMethods;

namespace FluentFlyout.Windows;

/// <summary>
/// Interaction logic for TaskbarWindow.xaml
/// </summary>
public partial class TaskbarWindow : Window
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private readonly DispatcherTimer _timer;
    private readonly int _nativeWidgetsPadding = 216;
    private readonly double _scale = 0.9;

    private IntPtr _trayHandle;
    private AutomationElement? _widgetElement;
    private AutomationElement? _trayElement;
    private AutomationElement? _taskbarFrameElement;
    // reference to main window for flyout functions
    private MainWindow? _mainWindow;
    private int _lastSelectedMonitor = -1;
    private bool _positionUpdateInProgress;
    private readonly Dictionary<string, Task> _pendingAutomationTasks = [];

    public TaskbarWindow()
    {
        WindowHelper.SetNoActivate(this);
        InitializeComponent();
        WindowHelper.SetTopmost(this);

        // Set DataContext for bindings
        DataContext = SettingsManager.Current;

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
        Widget.SetMainWindow(_mainWindow);
        TaskbarVisualizer.SetMainWindow(_mainWindow);
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
            IntPtr taskbarWindowHandle = interop.Handle;

            //Background = _hitTestTransparent; // ensures that non-content areas also trigger MouseEnter event

            IntPtr taskbarHandle = GetSelectedTaskbarHandle(out bool isMainTaskbarSelected);

            // This prevents the window from trying to float above the taskbar as a separate entity
            int style = GetWindowLong(taskbarWindowHandle, GWL_STYLE);
            style = (style & ~WS_POPUP) | WS_CHILD;
            SetWindowLong(taskbarWindowHandle, GWL_STYLE, style);

            SetParent(taskbarWindowHandle, taskbarHandle); // if this window is created faster than the Taskbar is loaded, then taskbarHandle will be NULL.

            CalculateAndSetPosition(taskbarHandle, taskbarWindowHandle, isMainTaskbarSelected);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Taskbar Widget error during setup");
        }
    }

    private void UpdateWindowRegion(IntPtr windowHandle, params Rect[] rects)
    {
        IntPtr rgn = CreateRectRgn(0, 0, 0, 0);
        foreach (var r in rects)
        {
            IntPtr newRgn = CreateRectRgn((int)r.Left, (int)r.Top, (int)r.Right, (int)r.Bottom);
            if (newRgn == IntPtr.Zero)
            {
                Logger.Error($"Taskbar Widget error during CreateRectRgn({(int)r.Left}, {(int)r.Top}, {(int)r.Right}, {(int)r.Bottom}).");
                goto on_error;
            }

            if (CombineRgn(rgn, rgn, newRgn, 2 /*RGN_OR*/) == 0)
            {
                Logger.Error($"Taskbar Widget error during CombineRgn. Combined regions: {string.Join(", ", rects.Select(i => $"RECT({(int)i.Left}, {(int)i.Top}, {(int)i.Right}, {(int)i.Bottom})"))}");
                DeleteObject(newRgn);
                goto on_error;
            }

            DeleteObject(newRgn);
        }

        if (SetWindowRgn(windowHandle, rgn, true) == 0)
        {
            Logger.Error($"Taskbar Widget error during SetWindowRgn.");
            goto on_error;
        }

        // Simple debugging to display the window region:
#if false
        var whiteRect = WidgetCanvas.Children.Cast<FrameworkElement>().FirstOrDefault(e => e.Name == "test_border");
        if (whiteRect == null)
        {
            whiteRect = new System.Windows.Shapes.Rectangle() { Name = "test_border", Width = 20000, Height = 20000, Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Black) };
            WidgetCanvas.Children.Add(whiteRect);
            Canvas.SetLeft(whiteRect, -10000);
            Canvas.SetTop(whiteRect, -10000);
        }
#endif

        return;

    on_error:

        // All regions that were not sent without errors to SetWindowRgn must be destroyed manually
        DeleteObject(rgn);
        if (SetWindowRgn(windowHandle, IntPtr.Zero, true) == 0)
            Logger.Error("Taskbar Widget error during window region reset.");
    }

    private void UpdatePosition()
    {
        if (MainWindow.ExplorerRestarting)
        {
            // Explorer is restarting -- do NOTHING
            return;
        }

        // Check premium status before allowing widget to be displayed
        if (!SettingsManager.Current.TaskbarWidgetEnabled || !SettingsManager.Current.IsPremiumUnlocked)
            return;

        try
        {
            var interop = new WindowInteropHelper(this);
            IntPtr taskbarHandle = GetSelectedTaskbarHandle(out bool isMainTaskbarSelected);

            if (interop.Handle == IntPtr.Zero)
            {
                if (MainWindow.ExplorerRestarting)
                {
                    Logger.Info("Skipping TaskbarWindow recovery during Explorer restart");
                    return;
                }

                _timer.Stop();

                Dispatcher.BeginInvoke(() =>
                {
                    try
                    {
                        _mainWindow?.RecreateTaskbarWindow();
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "Failed to signal MainWindow to recover Taskbar Widget window");
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

    private void CalculateAndSetPosition(IntPtr taskbarHandle, IntPtr taskbarWindowHandle, bool isMainTaskbarSelected)
    {
        // Prevent overlapping updates - if a previous update is still running
        // (e.g. waiting for an automation query timeout), skip this tick.
        if (_positionUpdateInProgress)
            return;
        _positionUpdateInProgress = true;

        try
        {
            // get DPI scaling
            double dpiScale = GetDpiForWindow(taskbarHandle) / 96.0;

            // Guard against invalid DPI (e.g. during explorer restart when handle is stale)
            if (dpiScale <= 0)
                return;

            // Get Taskbar dimensions
            RECT taskbarRect;
            // first, try to find the Taskbar.TaskbarFrame element in the XAML
            // this should give us the actual bounds of the taskbar, excluding invisible margins on some Windows configurations
            (bool success, Rect result) = GetTaskbarFrameRect(taskbarHandle);
            if (success)
            {
                taskbarRect = new RECT
                {
                    Left = (int)result.Left,
                    Top = (int)result.Top,
                    Right = (int)result.Right,
                    Bottom = (int)result.Bottom
                };
            }
            else
            {
                // fallback to GetWindowRect if we fail to get the frame bounds for some reason
                GetWindowRect(taskbarHandle, out taskbarRect);
            }

            int taskbarHeight = taskbarRect.Bottom - taskbarRect.Top;
            int taskbarWidth = taskbarRect.Right - taskbarRect.Left;

            int containerWidth = taskbarWidth;
            int containerHeight = taskbarHeight;

            // Following SetWindowPos will set the position relative to the parent window,
            // so those coordinates need to be converted.
            POINT containerPos = new() { X = taskbarRect.Left, Y = taskbarRect.Top };
            ScreenToClient(taskbarHandle, ref containerPos);

            // Apply using SetWindowPos (Bypassing WPF layout engine)
            SetWindowPos(taskbarWindowHandle, 0,
                     containerPos.X, containerPos.Y,
                     containerWidth, containerHeight,
                     SWP_NOZORDER | SWP_NOACTIVATE | SWP_ASYNCWINDOWPOS | SWP_SHOWWINDOW);

            var wRect = PositionWidget(taskbarHandle, taskbarRect, dpiScale, isMainTaskbarSelected);
            var vRect = PositionVisualizer(taskbarHandle, taskbarRect, dpiScale, isMainTaskbarSelected);

            UpdateWindowRegion(taskbarWindowHandle, wRect, vRect);

            _lastSelectedMonitor = SettingsManager.Current.TaskbarWidgetSelectedMonitor;
        }
        finally
        {
            _positionUpdateInProgress = false;
        }
    }

    private Rect PositionWidget(IntPtr taskbarHandle, RECT taskbarRect, double dpiScale, bool isMainTaskbarSelected)
    {
        if (!SettingsManager.Current.TaskbarWidgetEnabled)
            return Rect.Empty;

        // Calculate widget size
        var (logicalWidth, logicalHeight) = Widget.CalculateSize(dpiScale);

        int physicalWidth = (int)(logicalWidth * dpiScale * _scale);
        int physicalHeight = (int)(logicalHeight * dpiScale);

        int taskbarHeight = taskbarRect.Bottom - taskbarRect.Top;

        // Calculate vertical position (centered in taskbar)
        int widgetTop = (taskbarHeight - physicalHeight) / 2;

        // Calculate horizontal position based on alignment setting
        int widgetLeft = 0;
        switch (SettingsManager.Current.TaskbarWidgetPosition)
        {
            case 0: // left aligned with some padding (like native widgets)
                widgetLeft = 20;

                if (SettingsManager.Current.TaskbarVisualizerEnabled && SettingsManager.Current.TaskbarVisualizerPosition == 0)
                    widgetLeft += (int)(TaskbarVisualizer.Width * dpiScale) + 4;

                if (!SettingsManager.Current.TaskbarWidgetPadding)
                    break;

                // automatic widget padding to the left
                try
                {
                    // find widget button in XAML
                    (bool found, Rect widgetRect) = GetTaskbarWidgetRect(taskbarHandle);

                    // make sure it's on the left side, otherwise ignore (widget might be to the right)
                    if (found && widgetRect.Right < (taskbarRect.Left + taskbarRect.Right) / 2)
                    {
                        // Convert absolute screen position to relative position within taskbar
                        widgetLeft = (int)(widgetRect.Right - taskbarRect.Left) + 2;
                    }
                }
                catch (Exception ex)
                {
                    // fallback to default padding
                    Logger.Warn(ex, "Failed to get Widgets button position.");
                    widgetLeft += _nativeWidgetsPadding + 2;
                }
                break;

            case 1: // center of the taskbar
                widgetLeft = (taskbarRect.Right - taskbarRect.Left - physicalWidth) / 2;

                if (SettingsManager.Current.TaskbarVisualizerEnabled)
                    if (SettingsManager.Current.TaskbarVisualizerPosition == 0)
                        widgetLeft += (int)(TaskbarVisualizer.Width * dpiScale) / 2 + 4;
                    else
                        widgetLeft -= (int)(TaskbarVisualizer.Width * dpiScale) / 2 - 4;

                break;

            case 2: // right aligned next to system tray with tiny bit of padding
                try
                {
                    if (SettingsManager.Current.TaskbarVisualizerEnabled && SettingsManager.Current.TaskbarVisualizerPosition == 1)
                        widgetLeft -= (int)(TaskbarVisualizer.Width * dpiScale) - 4;

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
                                // Convert absolute screen position to relative position within taskbar
                                widgetLeft += (int)(widgetRect.Left - taskbarRect.Left) - 1 - physicalWidth;
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
                            // Convert absolute screen position to relative position within taskbar
                            widgetLeft += (int)(secondaryTrayRect.Left - taskbarRect.Left) - physicalWidth - 1;
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
                        widgetLeft += taskbarRect.Right - taskbarRect.Left - physicalWidth - 20;
                        break;
                    }
                    GetWindowRect(_trayHandle, out RECT trayRect);
                    // Convert absolute screen position to relative position within taskbar
                    widgetLeft += trayRect.Left - taskbarRect.Left - physicalWidth - 1;
                }
                catch (Exception ex)
                {
                    // Fallback to left alignment
                    Logger.Warn(ex, "Failed to get System Tray position.");
                    widgetLeft = 20;
                }
                break;
        }

        widgetLeft += SettingsManager.Current.TaskbarWidgetManualPadding;

        // Set widget position within canvas
        Canvas.SetLeft(Widget, widgetLeft / dpiScale);
        Canvas.SetTop(Widget, widgetTop / dpiScale);
        Widget.Width = physicalWidth / dpiScale;
        Widget.Height = physicalHeight / dpiScale;

        return new Rect(Canvas.GetLeft(Widget) * dpiScale, Canvas.GetTop(Widget) * dpiScale, Widget.Width * dpiScale, Widget.Height * dpiScale);
    }

    private Rect PositionVisualizer(IntPtr taskbarHandle, RECT taskbarRect, double dpiScale, bool isMainTaskbarSelected)
    {
        if (!SettingsManager.Current.TaskbarVisualizerEnabled)
            return Rect.Empty;

        int taskbarHeight = taskbarRect.Bottom - taskbarRect.Top;
        int visualizerTop = (taskbarHeight - (int)(TaskbarVisualizer.Height * dpiScale)) / 2;

        int visualizerLeft = 0;
        switch (SettingsManager.Current.TaskbarVisualizerPosition)
        {
            case 0: // left aligned next to widget
                visualizerLeft = (int)(Canvas.GetLeft(Widget) * dpiScale) - (int)(TaskbarVisualizer.Width * dpiScale);
                break;

            case 1: // right aligned next to widget
                visualizerLeft = (int)(Canvas.GetLeft(Widget) * dpiScale) + (int)(Widget.Width * dpiScale);
                break;
        }

        // Set visualizer position within canvas
        Canvas.SetLeft(TaskbarVisualizer, visualizerLeft / dpiScale);
        Canvas.SetTop(TaskbarVisualizer, visualizerTop / dpiScale);

        return new Rect(Canvas.GetLeft(TaskbarVisualizer) * dpiScale, Canvas.GetTop(TaskbarVisualizer) * dpiScale, TaskbarVisualizer.Width * dpiScale, TaskbarVisualizer.Height * dpiScale);
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

        // Delegate UI update to widget control
        Widget.UpdateUi(title, artist, icon, playbackStatus, playbackControls);

        // Update position after UI change
        Dispatcher.BeginInvoke(() => UpdatePosition(), DispatcherPriority.Background);

        Dispatcher.Invoke(() =>
        {
            Visibility = Visibility.Visible;
        });
    }

    private (bool, Rect) GetTaskbarXamlElementRect(IntPtr taskbarHandle, ref AutomationElement? elementCache, string elementName)
    {
        if (taskbarHandle == IntPtr.Zero)
            return (false, Rect.Empty);

        try
        {
            // reset if monitor changed
            if (_lastSelectedMonitor != SettingsManager.Current.TaskbarWidgetSelectedMonitor)
                elementCache = null;

            // find widget in XAML
            if (elementCache == null)
            {
                if (_pendingAutomationTasks.TryGetValue(elementName, out var pendingTask) && !pendingTask.IsCompleted)
                    return (false, Rect.Empty);

                AutomationElement? found = null;
                var findTask = Task.Run(() =>
                {
                    var root = AutomationElement.FromHandle(taskbarHandle);
                    found = root.FindFirst(TreeScope.Descendants,
                        new PropertyCondition(AutomationElement.AutomationIdProperty, elementName));
                });
                _pendingAutomationTasks[elementName] = findTask;

                if (!findTask.Wait(1000))
                {
                    Logger.Warn("Timeout querying taskbar XAML element: " + elementName);
                    return (false, Rect.Empty);
                }

                // Propagate any exception from the background thread
                findTask.GetAwaiter().GetResult();
                elementCache = found;
            }

            if (elementCache == null) // widget most likely disabled
                return (false, Rect.Empty);

            try
            {
                if (_pendingAutomationTasks.TryGetValue(elementName, out var pendingTask) && !pendingTask.IsCompleted)
                {
                    elementCache = null;
                    return (false, Rect.Empty);
                }

                var cachedElement = elementCache;
                var boundsTask = Task.Run(() => cachedElement.Current.BoundingRectangle);
                _pendingAutomationTasks[elementName] = boundsTask;

                if (!boundsTask.Wait(500))
                {
                    Logger.Warn("Timeout getting bounds for taskbar XAML element: " + elementName);
                    elementCache = null;
                    return (false, Rect.Empty);
                }

                Rect elementRect = boundsTask.GetAwaiter().GetResult();

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
            Logger.Warn(ex, "COM error retrieving taskbar XAML element Rect: " + elementName);
            elementCache = null; // reset cache on error
            return (false, Rect.Empty);
        }
        catch (ElementNotAvailableException)
        {
            Logger.Warn("Taskbar XAML element not available, resetting cache: " + elementName);
            elementCache = null;
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

    private (bool, Rect) GetTaskbarFrameRect(IntPtr taskbarHandle)
    {
        return GetTaskbarXamlElementRect(taskbarHandle, ref _taskbarFrameElement, "TaskbarFrame");
    }
}