// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF;
using FluentFlyoutWPF.Classes;
using FluentFlyoutWPF.Classes.Utils;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
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

    private sealed class AutomationElementCacheEntry
    {
        public AutomationElement? Element;
        public Rect Bounds = Rect.Empty;
        public DateTime LastBoundsTime = DateTime.MinValue;
        public DateTime NegativeCacheUntil = DateTime.MinValue;
        public bool IsSearching;
        public bool IsQueryingBounds;
    }

    private IntPtr _trayHandle;
    private readonly Dictionary<string, AutomationElementCacheEntry> _automationCaches = [];
    // reference to main window for flyout functions
    private MainWindow? _mainWindow;
    private int _lastSelectedMonitor = -1;
    private bool _positionUpdateInProgress;

    private GlobalSystemMediaTransportControlsSessionPlaybackStatus? _lastPlaybackStatus;
    private DispatcherTimer? _autoHideTimer;

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

        SettingsManager.Current.PropertyChanged += (s, args) =>
        {
            Dispatcher.Invoke(() =>
            {
                UpdatePosition();
                _mainWindow?.UpdateTaskbar();
            });
        };
    }

    private IntPtr GetSelectedTaskbarHandle(out bool isMainTaskbarSelected)
    {
        var monitors = MonitorUtil.GetMonitors();
        var selectedMonitor = monitors[Math.Clamp(SettingsManager.Current.TaskbarWidgetSelectedMonitor, 0, monitors.Count - 1)];
        isMainTaskbarSelected = true;

        // Get the main taskbar and check if it is on the selected monitor.
        var mainHwnd = FindWindow("Shell_TrayWnd", null);
        if (MonitorUtil.GetMonitor(mainHwnd).deviceId == selectedMonitor.deviceId)
            return mainHwnd;

        if (monitors.Count == 1)
            return mainHwnd;

        isMainTaskbarSelected = false;
        if (monitors.Count == 2)
        {
            var hwnd = FindWindow("Shell_SecondaryTrayWnd", null);
            if (MonitorUtil.GetMonitor(hwnd).deviceId == selectedMonitor.deviceId)
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
                if (MonitorUtil.GetMonitor(wnd).deviceId == selectedMonitor.deviceId)
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
            // make sure rect is not empty - happens when setting elements to collapsed
            if (r == Rect.Empty)
                continue;

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

            if (!SettingsManager.Current.LegacyTaskbarWidthEnabled)
            {
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
            }
            else
            {
                // legacy method - GetWindowRect on the entire taskbar, which includes invisible margins on some Windows configurations
                GetWindowRect(taskbarHandle, out taskbarRect);
            }

            int taskbarHeight = taskbarRect.Bottom - taskbarRect.Top;
            int taskbarWidth = taskbarRect.Right - taskbarRect.Left;

            // Vertical taskbar support: rotate and reposition widget when taskbar is taller than wide
            bool isVertical = taskbarHeight > taskbarWidth;
            int containerWidth = taskbarWidth;
            int containerHeight = taskbarHeight;

            // Following SetWindowPos will set the position relative to the parent window,
            // so those coordinates need to be converted.
            POINT containerPos = new() { X = taskbarRect.Left, Y = taskbarRect.Top };
            ScreenToClient(taskbarHandle, ref containerPos);

            // Apply using SetWindowPos (Bypassing WPF layout engine).
            // HWND_TOP keeps this child window at the top of the taskbar's child z-order:
            // Explorer's taskbar XAML content bridge (Windows.UI.Composition.DesktopWindowContentBridge)
            // spans the whole taskbar and otherwise ends up above this window, hiding the widget.
            // Do NOT pass SWP_NOZORDER here, it would turn hWndInsertAfter into a no-op.
            SetWindowPos(taskbarWindowHandle, HWND_TOP,
                     containerPos.X, containerPos.Y,
                     containerWidth, containerHeight,
                     SWP_NOACTIVATE | SWP_ASYNCWINDOWPOS | SWP_SHOWWINDOW);
            var qRect = PositionQuickActions(taskbarHandle, taskbarRect, dpiScale, isMainTaskbarSelected, isVertical);
            var wRect = PositionWidget(taskbarHandle, taskbarRect, dpiScale, isMainTaskbarSelected, isVertical);
            var vRect = PositionVisualizer(taskbarHandle, taskbarRect, dpiScale, isMainTaskbarSelected, isVertical);

            UpdateWindowRegion(taskbarWindowHandle, qRect, wRect, vRect);

            _lastSelectedMonitor = SettingsManager.Current.TaskbarWidgetSelectedMonitor;
        }
        finally
        {
            _positionUpdateInProgress = false;
        }
    }

    private Rect PositionWidget(IntPtr taskbarHandle, RECT taskbarRect, double dpiScale, bool isMainTaskbarSelected, bool isVertical)
    {
        if (!SettingsManager.Current.TaskbarWidgetEnabled)
            return Rect.Empty;

        // Calculate widget size
        var (logicalWidth, logicalHeight) = Widget.CalculateSize(dpiScale);

        int physicalWidth = (int)(logicalWidth * dpiScale * _scale);
        int physicalHeight = (int)(logicalHeight * dpiScale);

        int taskbarHeight = taskbarRect.Bottom - taskbarRect.Top;
        int taskbarWidth = taskbarRect.Right - taskbarRect.Left;

        // Apply orientation transform
        Widget.LayoutTransform = isVertical ? new System.Windows.Media.RotateTransform(90) : null;
        Widget.RenderTransform = System.Windows.Media.Transform.Identity;
        Widget.SetVerticalMode(isVertical);

        int primarySize = isVertical ? taskbarHeight : taskbarWidth;
        int crossSize = isVertical ? taskbarWidth : taskbarHeight;

        int crossPos = (crossSize - physicalHeight) / 2;
        int primaryPos = 0;

        switch (SettingsManager.Current.TaskbarWidgetPosition)
        {
            case 0: // near start (left for horizontal, top for vertical)
                primaryPos = 20;

                int quickActionsWidth = (SettingsManager.Current.TaskbarMixerButtonEnabled ? 40 : 0) + (SettingsManager.Current.TaskbarClipboardButtonEnabled ? 40 : 0);
                if (quickActionsWidth > 0)
                {
                    primaryPos += quickActionsWidth + 4;
                }

                if (SettingsManager.Current.TaskbarVisualizerEnabled && SettingsManager.Current.TaskbarVisualizerPosition == 0)
                    primaryPos += (int)(TaskbarVisualizer.Width * dpiScale) + 4;

                if (!SettingsManager.Current.TaskbarWidgetPadding)
                    break;;

                // automatic widget padding to the start
                try
                {
                    // find widget button in XAML
                    (bool found, Rect nativeWidgetRect) = GetTaskbarWidgetRect(taskbarHandle);

                    // Accept only if the native Widgets button is in the start half of the taskbar
                    bool inStartHalf = isVertical
                        ? nativeWidgetRect.Bottom < (taskbarRect.Top + taskbarRect.Bottom) / 2.0
                        : nativeWidgetRect.Right < (taskbarRect.Left + taskbarRect.Right) / 2.0;

                    if (found && inStartHalf && nativeWidgetRect.Width > 0 && nativeWidgetRect.Height > 0)
                    {
                        // Convert absolute screen position to relative position within taskbar
                        primaryPos = isVertical
                            ? (int)(nativeWidgetRect.Bottom - taskbarRect.Top) + 2
                            : (int)(nativeWidgetRect.Right - taskbarRect.Left) + 2;

                        if (quickActionsWidth > 0)
                        {
                            primaryPos += quickActionsWidth + 4;
                        }

                        if (SettingsManager.Current.TaskbarVisualizerEnabled && SettingsManager.Current.TaskbarVisualizerPosition == 0)
                            primaryPos += (int)(TaskbarVisualizer.Width * dpiScale) + 4;
                    }
                }
                catch (Exception ex)
                {
                    // fallback to default padding
                    Logger.Warn(ex, "Failed to get Widgets button position.");
                    primaryPos += _nativeWidgetsPadding + 2;
                }
                break;

            case 1: // center of the taskbar
                primaryPos = (primarySize - physicalWidth) / 2;

                if (SettingsManager.Current.TaskbarVisualizerEnabled)
                    if (SettingsManager.Current.TaskbarVisualizerPosition == 0)
                        primaryPos += (int)(TaskbarVisualizer.Width * dpiScale) / 2 + 4;
                    else
                        primaryPos -= (int)(TaskbarVisualizer.Width * dpiScale) / 2 - 4;
                break;

            case 2: // near end (right for horizontal, bottom for vertical)
                try
                {
                    if (SettingsManager.Current.TaskbarVisualizerEnabled && SettingsManager.Current.TaskbarVisualizerPosition == 1)
                        primaryPos -= (int)(TaskbarVisualizer.Width * dpiScale) - 4;

                    // Horizontal only: try to position next to native Widgets button on the end side
                    if (!isVertical && SettingsManager.Current.TaskbarWidgetPadding)
                    {
                        try
                        {
                            // find widget button in XAML
                            (bool found, Rect nativeWidgetRect) = GetTaskbarWidgetRect(taskbarHandle);

                            // make sure it's on the right side, otherwise ignore (widget might be to the left)
                            if (found && nativeWidgetRect.Left > (taskbarRect.Left + taskbarRect.Right) / 2.0)
                            {
                                // Convert absolute screen position to relative position within taskbar
                                primaryPos += (int)(nativeWidgetRect.Left - taskbarRect.Left) - 1 - physicalWidth;
                                break;
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
                        (bool found, Rect trayRect) = GetSystemTrayRect(taskbarHandle);

                        if (found)
                        {
                            // Convert absolute screen position to relative position within taskbar
                            double trayOffset = isVertical
                                ? trayRect.Top - taskbarRect.Top
                                : trayRect.Left - taskbarRect.Left;
                            primaryPos += (int)trayOffset - physicalWidth - (isVertical ? 2 : 1);
                            break;
                        }
                    }
                    else
                    {
                        // Primary taskbar: for vertical, try automation first (more reliable on ExplorerPatcher)
                        if (isVertical)
                        {
                            (bool trayFound, Rect trayAutomationRect) = GetSystemTrayRect(taskbarHandle);
                            if (trayFound && trayAutomationRect.Top >= taskbarRect.Top)
                            {
                                primaryPos += (int)(trayAutomationRect.Top - taskbarRect.Top) - physicalWidth - 2;
                                break;
                            }
                        }

                        // Primary taskbar: TrayNotifyWnd (original approach for horizontal, fallback for vertical)
                        if (_trayHandle == IntPtr.Zero || _lastSelectedMonitor != SettingsManager.Current.TaskbarWidgetSelectedMonitor)
                            _trayHandle = FindWindowEx(taskbarHandle, IntPtr.Zero, "TrayNotifyWnd", null);

                        if (_trayHandle != IntPtr.Zero)
                        {
                            GetWindowRect(_trayHandle, out RECT trayWndRect);
                            // Convert absolute screen position to relative position within taskbar
                            double trayOffset = isVertical
                                ? trayWndRect.Top - taskbarRect.Top
                                : trayWndRect.Left - taskbarRect.Left;

                            // For vertical: validate the tray is in the lower half of the taskbar
                            if (!isVertical || trayOffset > taskbarHeight / 2)
                            {
                                primaryPos += (int)trayOffset - physicalWidth - (isVertical ? 2 : 6); // trayOffset isn't 100% accurate, so we subtract a few pixels
                                break;
                            }
                        }
                        else if (!isVertical)
                        {
                            // TrayNotifyWnd not found on horizontal: fallback to right alignment,
                            // since we are aligning to the right side and know the size of the taskbar.
                            primaryPos += taskbarWidth - physicalWidth - 20;
                            break;
                        }
                    }

                    // Final fallback: place near the end of the taskbar
                    primaryPos += primarySize - physicalWidth - 20;
                }
                catch (Exception ex)
                {
                    // Fallback to left alignment
                    Logger.Warn(ex, "Failed to get System Tray position.");
                    primaryPos = isVertical ? primarySize - physicalWidth - 20 : 20;
                }
                break;
        }

        primaryPos += SettingsManager.Current.TaskbarWidgetManualPadding;

        // Set widget position within canvas
        // primaryPos → left (horizontal) or top (vertical); crossPos → top (horizontal) or left (vertical)
        Canvas.SetLeft(Widget, (isVertical ? crossPos : primaryPos) / dpiScale);
        Canvas.SetTop(Widget, (isVertical ? primaryPos : crossPos) / dpiScale);
        Widget.Width = physicalWidth / dpiScale;
        Widget.Height = physicalHeight / dpiScale;

        // After 90° LayoutTransform the visual bounding rect has swapped dimensions
        double rectW = isVertical ? physicalHeight : physicalWidth;
        double rectH = isVertical ? physicalWidth : physicalHeight;
        return new Rect(Canvas.GetLeft(Widget) * dpiScale, Canvas.GetTop(Widget) * dpiScale, rectW, rectH);
    }

    private Rect PositionVisualizer(IntPtr taskbarHandle, RECT taskbarRect, double dpiScale, bool isMainTaskbarSelected, bool isVertical)
    {
        if (!SettingsManager.Current.TaskbarVisualizerEnabled)
            return Rect.Empty;

        double visualizerWidth = 84;

        TaskbarVisualizer.Visibility = Visibility.Visible;
        TaskbarVisualizer.Width = visualizerWidth;

        // Rotate visualizer 90° on vertical taskbar so it fits the slim width
        TaskbarVisualizer.LayoutTransform = isVertical ? new System.Windows.Media.RotateTransform(90) : null;

        int taskbarHeight = taskbarRect.Bottom - taskbarRect.Top;
        int taskbarWidth = taskbarRect.Right - taskbarRect.Left;

        int crossSize = isVertical ? taskbarWidth : taskbarHeight;
        int crossOffset = isVertical ? 0 : -1;
        int crossPos = (crossSize - (int)(TaskbarVisualizer.Height * dpiScale)) / 2 + crossOffset;

        double widgetPrimaryStart = isVertical ? Canvas.GetTop(Widget) : Canvas.GetLeft(Widget);
        int primaryPos;

        switch (SettingsManager.Current.TaskbarVisualizerPosition)
        {
            case 0: // before widget (left for horizontal, above for vertical)
                primaryPos = (int)(widgetPrimaryStart * dpiScale) - (int)(visualizerWidth * dpiScale);
                break;

            case 1: // after widget (right for horizontal, below for vertical)
                primaryPos = (int)(widgetPrimaryStart * dpiScale) + (int)(Widget.Width * dpiScale);
                break;

            default:
                primaryPos = 0;
                break;
        }

        // Set visualizer position within canvas
        Canvas.SetLeft(TaskbarVisualizer, (isVertical ? crossPos : primaryPos) / dpiScale);
        Canvas.SetTop(TaskbarVisualizer, (isVertical ? primaryPos : crossPos) / dpiScale);

        double rectW = isVertical ? TaskbarVisualizer.Height * dpiScale : visualizerWidth * dpiScale;
        double rectH = isVertical ? visualizerWidth * dpiScale : TaskbarVisualizer.Height * dpiScale;
        return new Rect(Canvas.GetLeft(TaskbarVisualizer) * dpiScale, Canvas.GetTop(TaskbarVisualizer) * dpiScale, rectW, rectH);
    }

    private Rect PositionQuickActions(IntPtr taskbarHandle, RECT taskbarRect, double dpiScale, bool isMainTaskbarSelected, bool isVertical)
    {
        bool hasQuickActions = SettingsManager.Current.TaskbarMixerButtonEnabled || SettingsManager.Current.TaskbarClipboardButtonEnabled;
        if (!hasQuickActions)
        {
            QuickActionsPanel.Visibility = Visibility.Collapsed;
            return Rect.Empty;
        }

        QuickActionsPanel.Visibility = Visibility.Visible;

        int taskbarHeight = taskbarRect.Bottom - taskbarRect.Top;
        int taskbarWidth = taskbarRect.Right - taskbarRect.Left;
        int crossSize = isVertical ? taskbarWidth : taskbarHeight;
        int crossPos = (crossSize - (int)(36 * dpiScale)) / 2;

        int quickActionsWidth = (SettingsManager.Current.TaskbarMixerButtonEnabled ? 40 : 0) + (SettingsManager.Current.TaskbarClipboardButtonEnabled ? 40 : 0);
        int primaryPos = 12;

        if (SettingsManager.Current.TaskbarWidgetPadding)
        {
            try
            {
                (bool found, Rect nativeWidgetRect) = GetTaskbarWidgetRect(taskbarHandle);
                bool inStartHalf = isVertical
                    ? nativeWidgetRect.Bottom < (taskbarRect.Top + taskbarRect.Bottom) / 2.0
                    : nativeWidgetRect.Right < (taskbarRect.Left + taskbarRect.Right) / 2.0;

                if (found && inStartHalf && nativeWidgetRect.Width > 0 && nativeWidgetRect.Height > 0)
                {
                    primaryPos = isVertical
                        ? (int)(nativeWidgetRect.Bottom - taskbarRect.Top) + 2
                        : (int)(nativeWidgetRect.Right - taskbarRect.Left) + 2;
                }
            }
            catch { }
        }

        double logicalLeft = (isVertical ? crossPos : primaryPos) / dpiScale;
        double logicalTop = (isVertical ? primaryPos : crossPos) / dpiScale;

        Canvas.SetLeft(QuickActionsPanel, logicalLeft);
        Canvas.SetTop(QuickActionsPanel, logicalTop);

        double physicalLeft = isVertical ? crossPos : primaryPos;
        double physicalTop = isVertical ? primaryPos : crossPos;
        double physicalW = (isVertical ? 36 : quickActionsWidth) * dpiScale;
        double physicalH = (isVertical ? quickActionsWidth : 36) * dpiScale;

        return new Rect(physicalLeft, physicalTop, physicalW, physicalH);
    }

    private static Rect GetElementLogicalScreenRect(FrameworkElement element)
    {
        try
        {
            Point first = element.PointToScreen(new Point(0, 0));
            Point second = element.PointToScreen(new Point(element.ActualWidth, element.ActualHeight));

            return new Rect(first.X, first.Y, second.X - first.X, second.Y - first.Y);
        }
        catch
        {
            return Rect.Empty;
        }
    }

    private static readonly System.Windows.Media.Color QuickActionIdleColor = System.Windows.Media.Color.FromArgb(1, 255, 255, 255);

    private void QuickAction_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is Border border)
        {
            WindowsThemeDetector.GetWindowsTheme(out _, out var systemTheme);
            bool isDark = systemTheme == WindowsThemeDetector.ThemeMode.Dark;
            SolidColorBrush targetBackgroundBrush = isDark
                ? new SolidColorBrush(System.Windows.Media.Color.FromArgb(197, 255, 255, 255)) { Opacity = 0.075 }
                : new SolidColorBrush(System.Windows.Media.Color.FromArgb(255, 255, 255, 255)) { Opacity = 0.6 };

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

            if (border.Background is not SolidColorBrush scb || scb.IsFrozen)
            {
                border.Background = new SolidColorBrush(QuickActionIdleColor);
            }

            border.Background.BeginAnimation(SolidColorBrush.ColorProperty, backgroundAnimation);
            border.Background.BeginAnimation(SolidColorBrush.OpacityProperty, backgroundOpacityAnimation);
        }
    }

    private void QuickAction_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is Border border)
        {
            var backgroundAnimation = new ColorAnimation
            {
                To = QuickActionIdleColor,
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };

            var backgroundOpacityAnimation = new DoubleAnimation
            {
                To = 1,
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };

            if (border.Background is not SolidColorBrush scb || scb.IsFrozen)
            {
                border.Background = new SolidColorBrush(QuickActionIdleColor);
            }

            border.Background.BeginAnimation(SolidColorBrush.ColorProperty, backgroundAnimation);
            border.Background.BeginAnimation(SolidColorBrush.OpacityProperty, backgroundOpacityAnimation);
        }
    }

    private void MixerButton_Click(object sender, RoutedEventArgs e)
    {
        IntPtr taskbarHandle = GetSelectedTaskbarHandle(out _);
        Rect taskbarRect;
        if (taskbarHandle != IntPtr.Zero && GetWindowRect(taskbarHandle, out RECT tr))
        {
            taskbarRect = new Rect(tr.Left, tr.Top, tr.Right - tr.Left, tr.Bottom - tr.Top);
        }
        else
        {
            taskbarRect = new Rect(0, SystemParameters.PrimaryScreenHeight - 40, SystemParameters.PrimaryScreenWidth, 40);
        }

        _mainWindow?.ShowVolumeMixerFromTaskbar(
            GetElementLogicalScreenRect(MixerButton),
            taskbarRect);
    }

    private void MixerButton_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        e.Handled = true;
        (_mainWindow ?? (MainWindow)Application.Current.MainWindow)?.AdjustMasterVolumeFromWheel(e.Delta);
    }

    private void ClipboardButton_Click(object sender, RoutedEventArgs e)
    {
        if (SettingsManager.Current.TaskbarClipboardMode == 1)
        {
            const byte vkLeftWindows = 0x5B;
            const byte vkV = 0x56;
            const uint keyUp = 0x0002;

            keybd_event(vkLeftWindows, 0, 0, IntPtr.Zero);
            keybd_event(vkV, 0, 0, IntPtr.Zero);
            keybd_event(vkV, 0, keyUp, IntPtr.Zero);
            keybd_event(vkLeftWindows, 0, keyUp, IntPtr.Zero);
            return;
        }

        IntPtr taskbarHandle = GetSelectedTaskbarHandle(out _);
        Rect taskbarRect;
        if (taskbarHandle != IntPtr.Zero && GetWindowRect(taskbarHandle, out RECT tr))
        {
            taskbarRect = new Rect(tr.Left, tr.Top, tr.Right - tr.Left, tr.Bottom - tr.Top);
        }
        else
        {
            taskbarRect = new Rect(0, SystemParameters.PrimaryScreenHeight - 40, SystemParameters.PrimaryScreenWidth, 40);
        }

        IntPtr returnFocusWindow = GetForegroundWindow();
        _mainWindow?.ToggleClipboardHistoryFromTaskbar(
            GetElementLogicalScreenRect(ClipboardButton),
            taskbarRect,
            returnFocusWindow);
    }

    public void UpdateUi(string title, string artist, BitmapImage? icon, GlobalSystemMediaTransportControlsSessionPlaybackStatus? playbackStatus, GlobalSystemMediaTransportControlsSessionPlaybackControls? playbackControls = null)
    {
        bool hasQuickActions = SettingsManager.Current.TaskbarMixerButtonEnabled || SettingsManager.Current.TaskbarClipboardButtonEnabled;
        bool hasVisualizer = SettingsManager.Current.TaskbarVisualizerEnabled;
        bool hasWidget = SettingsManager.Current.TaskbarWidgetEnabled;

        // Check premium status - hide window if nothing is enabled or not unlocked
        if ((!hasWidget && !hasVisualizer && !hasQuickActions) || !SettingsManager.Current.IsPremiumUnlocked)
        {
            if (_timer.IsEnabled)
                _timer.Stop();

            Dispatcher.Invoke(() =>
            {
                Visibility = Visibility.Collapsed;
            });
            return;
        }

        _lastPlaybackStatus = playbackStatus;

        // Autohide Widget independently when media is paused
        if (hasWidget && SettingsManager.Current.TaskbarWidgetAutoHide)
        {
            if (playbackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
            {
                _autoHideTimer?.Stop();
                _autoHideTimer = null;

                Dispatcher.Invoke(() =>
                {
                    Widget.Visibility = Visibility.Visible;
                    Visibility = Visibility.Visible;
                });
            }
            else
            {
                if (_autoHideTimer == null)
                {
                    _autoHideTimer = new DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(750)
                    };

                    _autoHideTimer.Tick += (s, e) =>
                    {
                        _autoHideTimer.Stop();
                        _autoHideTimer = null;

                        if (_lastPlaybackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                        {
                            Dispatcher.Invoke(() =>
                            {
                                Widget.Visibility = Visibility.Collapsed;
                                if (!hasVisualizer && !hasQuickActions)
                                {
                                    Visibility = Visibility.Collapsed;
                                }
                                else
                                {
                                    UpdatePosition();
                                }
                            });
                        }
                    };

                    _autoHideTimer.Start();
                }
            }
        }
        else
        {
            Widget.Visibility = hasWidget ? Visibility.Visible : Visibility.Collapsed;
        }

        if (!_timer.IsEnabled)
            _timer.Start();

        if (hasWidget)
        {
            Widget.UpdateUi(title, artist, icon, playbackStatus, playbackControls);
        }

        Dispatcher.Invoke(() =>
        {
            Visibility = Visibility.Visible;
            Widget.UpdateLayout();
            UpdateLayout();
            UpdatePosition();
        });
    }

    private (bool, Rect) GetTaskbarXamlElementRect(IntPtr taskbarHandle, string elementName)
    {
        if (taskbarHandle == IntPtr.Zero)
            return (false, Rect.Empty);

        try
        {
            if (_lastSelectedMonitor != SettingsManager.Current.TaskbarWidgetSelectedMonitor)
            {
                _automationCaches.Clear();
            }

            if (!_automationCaches.TryGetValue(elementName, out var cache))
            {
                cache = new AutomationElementCacheEntry();
                _automationCaches[elementName] = cache;
            }

            DateTime now = DateTime.UtcNow;

            // If we have a cached element
            if (cache.Element != null)
            {
                // If cached bounds are fresh (less than 2.5 seconds old) and valid, return immediately
                if (now - cache.LastBoundsTime < TimeSpan.FromMilliseconds(2500) && cache.Bounds != Rect.Empty && cache.Bounds.Width > 0 && cache.Bounds.Height > 0)
                {
                    return (true, cache.Bounds);
                }

                // If bounds need refresh and not already querying in background, start background refresh
                if (!cache.IsQueryingBounds)
                {
                    cache.IsQueryingBounds = true;
                    var elem = cache.Element;
                    Task.Run(() =>
                    {
                        try
                        {
                            Rect b = elem.Current.BoundingRectangle;
                            Dispatcher.InvokeAsync(() =>
                            {
                                cache.IsQueryingBounds = false;
                                if (b != Rect.Empty && b.Width > 0 && b.Height > 0)
                                {
                                    cache.Bounds = b;
                                    cache.LastBoundsTime = DateTime.UtcNow;
                                }
                                else
                                {
                                    // Element bounds invalid or hidden
                                    cache.Element = null;
                                    cache.Bounds = Rect.Empty;
                                    cache.NegativeCacheUntil = DateTime.UtcNow.AddSeconds(20);
                                }
                            });
                        }
                        catch
                        {
                            Dispatcher.InvokeAsync(() =>
                            {
                                cache.IsQueryingBounds = false;
                                cache.Element = null;
                                cache.Bounds = Rect.Empty;
                                cache.NegativeCacheUntil = DateTime.UtcNow.AddSeconds(20);
                            });
                        }
                    });
                }

                // Return existing bounds if valid while background update is running
                if (cache.Bounds != Rect.Empty && cache.Bounds.Width > 0 && cache.Bounds.Height > 0)
                {
                    return (true, cache.Bounds);
                }

                return (false, Rect.Empty);
            }

            // Element is not cached. Check negative cache
            if (now < cache.NegativeCacheUntil)
            {
                return (false, Rect.Empty);
            }

            // Launch background search if not already searching
            if (!cache.IsSearching)
            {
                cache.IsSearching = true;
                Task.Run(() =>
                {
                    try
                    {
                        var root = AutomationElement.FromHandle(taskbarHandle);
                        var found = root.FindFirst(TreeScope.Descendants,
                            new PropertyCondition(AutomationElement.AutomationIdProperty, elementName));

                        Rect b = Rect.Empty;
                        if (found != null)
                        {
                            try { b = found.Current.BoundingRectangle; } catch { }
                        }

                        Dispatcher.InvokeAsync(() =>
                        {
                            cache.IsSearching = false;
                            if (found != null && b != Rect.Empty && b.Width > 0 && b.Height > 0)
                            {
                                cache.Element = found;
                                cache.Bounds = b;
                                cache.LastBoundsTime = DateTime.UtcNow;
                                UpdatePosition();
                            }
                            else
                            {
                                cache.Element = null;
                                cache.Bounds = Rect.Empty;
                                cache.NegativeCacheUntil = DateTime.UtcNow.AddSeconds(20);
                            }
                        });
                    }
                    catch
                    {
                        Dispatcher.InvokeAsync(() =>
                        {
                            cache.IsSearching = false;
                            cache.Element = null;
                            cache.Bounds = Rect.Empty;
                            cache.NegativeCacheUntil = DateTime.UtcNow.AddSeconds(20);
                        });
                    }
                });
            }

            return (false, Rect.Empty);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error retrieving taskbar XAML element Rect: " + elementName);
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
        return GetTaskbarXamlElementRect(taskbarHandle, "WidgetsButton");
    }

    private (bool, Rect) GetSystemTrayRect(IntPtr taskbarHandle)
    {
        return GetTaskbarXamlElementRect(taskbarHandle, "SystemTrayIcon");
    }

    private (bool, Rect) GetTaskbarFrameRect(IntPtr taskbarHandle)
    {
        return GetTaskbarXamlElementRect(taskbarHandle, "TaskbarFrame");
    }

    private static Rect GetElementScreenRect(FrameworkElement element)
    {
        Point first = element.PointToScreen(new Point(0, 0));
        Point second = element.PointToScreen(new Point(element.ActualWidth, element.ActualHeight));

        return new Rect(
            Math.Min(first.X, second.X),
            Math.Min(first.Y, second.Y),
            Math.Abs(second.X - first.X),
            Math.Abs(second.Y - first.Y));
    }

    public bool TryGetMediaWidgetScreenRect(out Rect rect)
    {
        rect = Rect.Empty;
        if (!SettingsManager.Current.TaskbarWidgetEnabled
            || Widget.Visibility != Visibility.Visible
            || Widget.ActualWidth <= 0
            || Widget.ActualHeight <= 0)
        {
            return false;
        }

        rect = GetElementScreenRect(Widget);
        return rect.Width > 0 && rect.Height > 0;
    }
}