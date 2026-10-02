// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes;
using FluentFlyout.Classes.Settings;
using FluentFlyout.Classes.Utils;
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

    private const double SmallTaskbarDetectionThreshold = 40;

    private readonly DispatcherTimer _timer;
    private readonly int _nativeWidgetsPadding = 216;
    private readonly double _scale = 0.9;

    /// <summary>
    /// The widget/visualizer gap in physical pixels. The gap is a logical constant, and logical
    /// widget units convert to physical px via dpiScale * _scale - the same factor used for the
    /// widget width itself. Both PositionWidget and PositionVisualizer must use this helper;
    /// adding the raw constant on one side only detaches the visualizer from the widget at any
    /// DPI other than 100%.
    /// </summary>
    private double VisualizerGapPx(double dpiScale) => WidgetLayoutSolver.VisualizerGap * dpiScale * _scale;

    private IntPtr _trayHandle;
    private AutomationElement? _widgetElement;
    private AutomationElement? _trayElement;
    private AutomationElement? _taskbarFrameElement;
    // reference to main window for flyout functions
    private MainWindow? _mainWindow;
    private int _lastSelectedMonitor = -1;
    private IntPtr _lastTaskbarHandle;
    private bool _positionUpdateInProgress;
    private bool _isClosing;
    private readonly Dictionary<string, Task> _pendingAutomationTasks = [];

    // adaptive width state
    private RECT _lastTaskbarRect;
    private Rect? _lastWidgetRect;
    private Rect? _lastVisualizerRect;
    private double _lastWindowStartPhysical;
    private double _lastWindowEndPhysical = -1;
    private readonly Lock _taskbarGroupRectLock = new();
    private bool _taskbarGroupFound;
    private Rect _taskbarGroupCluster = Rect.Empty;
    private IntPtr _taskbarGroupRectHandle;
    private DateTime _taskbarGroupRectTime = DateTime.MinValue;
    private Task? _taskbarGroupQueryTask;
    private bool _taskbarGroupStale;
    private bool _taskbarGroupLoggedOnce;

    // WinEvent hook: invalidates the group rect instantly on taskbar changes
    private const uint EventObjectShow = 0x8002;
    private const uint EventObjectHide = 0x8003;
    private const uint EventObjectReorder = 0x8004;
    private const uint EventObjectLocationChange = 0x800B;
    private const uint EventObjectStateChange = 0x800E;
    private const uint WINEVENT_OUTOFCONTEXT = 0;
    private const int GA_ROOT = 2;
    private IntPtr _winEventHook;
    private uint _winEventHookPid;
    private NativeMethods.WinEventProc? _winEventProc;
    private DateTime _lastGroupLocationEventUtc = DateTime.MinValue;

    // hysteresis band (physical px) that absorbs jitter between layout tiers
    private const double SpanHysteresis = 4;
    private double _debouncedSpanPhysical = -1;

    // width animation: 16ms DispatcherTimer pumps the frames (CompositionTarget.Rendering is
    // unreliable for this reparented window); every frame recomputes the full layout
    private const double WidthAnimationFrameMs = 16;

    // user-configurable duration (ms); clamped so absurd values can't stall or skip the run
    private double WidthAnimationDurationMs =>
        Math.Clamp(SettingsManager.Current.TaskbarWidgetWidthAnimationDurationMs, 100, 2000);
    private DateTime _animationStartUtc = DateTime.MinValue;
    private double _animationFromSpan;
    private double _animationTargetSpan = -1;
    private bool _animationActive;
    private int _animationFramesApplied;
    private DispatcherTimer? _widthAnimationTimer;

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
        _timer.Interval = TimeSpan.FromMilliseconds(500); // poll backstop; the WinEvent hook reacts instantly
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

    private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg is WM_DPICHANGED or WM_DPICHANGED_AFTERPARENT)
        {
            // WPF processes WM_DPICHANGED itself. Refresh placement after that layout
            // pass; PMv2 child windows receive the AFTERPARENT variant instead.
            Dispatcher.BeginInvoke(() =>
            {
                InvalidateMeasure();
                InvalidateArrange();
                InvalidateVisual();
                UpdateLayout();
                UpdatePosition();
            }, DispatcherPriority.Loaded);
        }

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
            case 0x0113: // WM_TIMER - the taskbar runs timers when Start menu and flyouts open
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
        InstallWinEventHook();
        _mainWindow = (MainWindow)Application.Current.MainWindow;
        Widget.SetMainWindow(_mainWindow);
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
            ResetTaskbarCachesIfHandleChanged(taskbarHandle);

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

    private void UpdatePosition(bool immediate = false)
    {
        if (_isClosing || MainWindow.ExplorerRestarting)
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
            ResetTaskbarCachesIfHandleChanged(taskbarHandle);

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
                if (immediate)
                {
                    CalculateAndSetPosition(taskbarHandle, interop.Handle, isMainTaskbarSelected);
                }
                else
                {
                    Dispatcher.BeginInvoke(() =>
                    {
                        CalculateAndSetPosition(taskbarHandle, interop.Handle, isMainTaskbarSelected);
                    }, DispatcherPriority.Background);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Taskbar Widget error during position update");
        }
    }

    private void ResetTaskbarCachesIfHandleChanged(IntPtr taskbarHandle)
    {
        if (_lastTaskbarHandle == taskbarHandle)
            return;

        _lastTaskbarHandle = taskbarHandle;
        _trayHandle = IntPtr.Zero;
        _widgetElement = null;
        _trayElement = null;
        _taskbarFrameElement = null;
        _pendingAutomationTasks.Clear();
        lock (_taskbarGroupRectLock)
        {
            _taskbarGroupRectHandle = IntPtr.Zero;
            _taskbarGroupFound = false;
            _taskbarGroupCluster = Rect.Empty;
            _taskbarGroupRectTime = DateTime.MinValue;
            _taskbarGroupStale = false;
            _taskbarGroupQueryTask = null;
        }
        _debouncedSpanPhysical = -1;

        // the taskbar handle is only known now, so (re)install the hook here rather than
        // relying on Window_Loaded, which runs before the first position update
        InstallWinEventHook();
    }

    /// <summary>
    /// Computes the free window (physical px) the widget + visualizer group may occupy.
    /// </summary>
    private void ComputeAdaptiveWindow(IntPtr taskbarHandle, RECT taskbarRect, double dpiScale, bool isMainTaskbarSelected, bool isVertical, int taskbarWidth, int taskbarHeight, out double windowStart, out double windowEnd)
    {
        int primarySize = isVertical ? taskbarHeight : taskbarWidth;
        double primaryMid = isVertical
            ? (taskbarRect.Top + taskbarRect.Bottom) / 2.0
            : (taskbarRect.Left + taskbarRect.Right) / 2.0;

        // Native Widgets button (only queried when automatic padding is on, like the legacy path)
        (bool widgetsFound, Rect widgetsRect) = SettingsManager.Current.TaskbarWidgetPadding
            ? GetTaskbarWidgetRect(taskbarHandle)
            : (false, Rect.Empty);
        double widgetBtnStart = 0, widgetBtnEnd = 0;
        bool widgetBtnInStartHalf = false, widgetBtnInEndHalf = false;
        if (widgetsFound)
        {
            widgetBtnStart = isVertical ? widgetsRect.Top - taskbarRect.Top : widgetsRect.Left - taskbarRect.Left;
            widgetBtnEnd = isVertical ? widgetsRect.Bottom - taskbarRect.Top : widgetsRect.Right - taskbarRect.Left;
            widgetBtnInStartHalf = isVertical ? widgetsRect.Bottom < primaryMid : widgetsRect.Right < primaryMid;
            widgetBtnInEndHalf = !isVertical && widgetsRect.Left > primaryMid;
        }

        // System tray start edge
        bool trayFound = TryFindTrayAnchor(taskbarHandle, taskbarRect, isMainTaskbarSelected, isVertical, allowAutomation: true, out double trayStart)
            && trayStart > 0 && trayStart < primarySize;

        // centered element group (fixed buttons + app icons) shifts sideways on center-aligned taskbars
        bool groupFound = false;
        double groupStart = 0, groupEnd = 0;
        var (groupRectFound, groupRect) = GetTaskbarGroupRect(taskbarHandle);
        if (groupRectFound)
        {
            groupStart = isVertical ? groupRect.Top - taskbarRect.Top : groupRect.Left - taskbarRect.Left;
            groupEnd = isVertical ? groupRect.Bottom - taskbarRect.Top : groupRect.Right - taskbarRect.Left;
            groupFound = groupEnd > groupStart && groupStart < primarySize && groupEnd > 0;
        }

        switch (SettingsManager.Current.TaskbarWidgetPosition)
        {
            case 1: // center: use the free gap that leaves the most room around the icon cluster
                {
                    windowStart = 20;
                    windowEnd = trayFound ? trayStart - 4 : primarySize - 20;

                    if (groupFound && groupStart < windowEnd && groupEnd > windowStart)
                    {
                        // the group covers the middle - fall back to the larger side gap
                        double leftGap = groupStart - 4 - windowStart;
                        double rightGap = windowEnd - (groupEnd + 4);
                        if (leftGap >= rightGap && leftGap > 0)
                            windowEnd = Math.Min(windowEnd, groupStart - 4);
                        else if (rightGap > 0)
                            windowStart = Math.Max(windowStart, groupEnd + 4);
                    }
                    break;
                }

            case 2: // near end: keep the legacy anchor, but never grow into the app icon cluster
                {
                    double rightLimit;
                    if (!isVertical && SettingsManager.Current.TaskbarWidgetPadding && widgetBtnInEndHalf)
                        rightLimit = widgetBtnStart - 1;
                    else if (trayFound)
                        rightLimit = trayStart - (isVertical ? 2 : isMainTaskbarSelected ? 6 : 1);
                    else
                        rightLimit = primarySize - 20;

                    windowEnd = rightLimit;
                    windowStart = 20;
                    if (groupFound && groupEnd + 4 < rightLimit)
                        windowStart = groupEnd + 4;
                    break;
                }

            default: // near start
                {
                    windowStart = SettingsManager.Current.TaskbarWidgetPadding && widgetBtnInStartHalf ? widgetBtnEnd + 2 : 20;
                    windowEnd = trayFound ? trayStart - 4 : primarySize - 20;

                    // keep clear of the centered group (fixed buttons + icons) whenever a gap exists
                    if (groupFound && groupStart - 4 > windowStart)
                        windowEnd = Math.Min(windowEnd, groupStart - 4);
                    break;
                }
        }

        if (windowEnd < windowStart)
        {
            if (!_wasTaskbarFull)
                Logger.Info($"Taskbar window collapsed: start={windowStart:F1} end={windowEnd:F1} " +
                    $"groupEnd={groupEnd:F1} trayStart={(trayFound ? trayStart.ToString("F1") : "not found")} " +
                    $"taskbarWidth={taskbarWidth}");
            _wasTaskbarFull = true;

            if (SettingsManager.Current.TaskbarWidgetHideWhenFull)
            {
                // taskbar 100% full: no gap fits even the icon - collapse to zero span so the
                // solver returns Hidden and the widget is removed instead of drawing over icons
                windowStart = 0;
                windowEnd = 0;
                _debouncedSpanPhysical = 0;
            }
            else
            {
                // geometry looks wrong (stale or bogus rects) - fall back to the unconstrained window
                windowStart = 0;
                windowEnd = primarySize;
            }
        }
        else
        {
            _wasTaskbarFull = false;
        }

        // spans within the hysteresis band reuse the previously applied width.
        // Skipped while a width animation runs - the debounce would rewrite the interpolated
        // span mid-run and stall the animation.
        double span = windowEnd - windowStart;
        if (!_animationActive && _debouncedSpanPhysical >= 0 && Math.Abs(span - _debouncedSpanPhysical) > 0.25
            && Math.Abs(span - _debouncedSpanPhysical) <= SpanHysteresis)
        {
            windowEnd = windowStart + _debouncedSpanPhysical;
            return;
        }

        _debouncedSpanPhysical = span;
    }

    /// <summary>
    /// Eases the computed free window span toward the target span when width animation is
    /// enabled, so the widget grows/shrinks smoothly instead of snapping between tiers.
    /// </summary>
    private void EaseWindowSpan(ref double windowStart, ref double windowEnd)
    {
        double targetSpan = windowEnd - windowStart;

        if (!SettingsManager.Current.TaskbarWidgetWidthAnimation || targetSpan <= 0)
        {
            // disabled or hidden (taskbar full): always snap
            StopWidthAnimation();
            _animationTargetSpan = -1;
            return;
        }

        if (_animationActive)
        {
            double progress = Math.Min((DateTime.UtcNow - _animationStartUtc).TotalMilliseconds / WidthAnimationDurationMs, 1);
            double eased = 1 - Math.Pow(1 - progress, 3); // ease-out cubic
            double currentSpan = _animationFromSpan + (_animationTargetSpan - _animationFromSpan) * eased;

            if (Math.Abs(targetSpan - _animationTargetSpan) >= 0.5)
            {
                // target moved mid-run (tier change): retarget from the current position
                _animationFromSpan = currentSpan;
                _animationTargetSpan = targetSpan;
                _animationStartUtc = DateTime.UtcNow;
                currentSpan = _animationFromSpan;
            }

            windowEnd = windowStart + currentSpan;
            return;
        }

        // start a new run from the last applied span - using the current target here would
        // make from == target and the animation would never start at all
        double fromSpan;
        if (_animationTargetSpan >= 0)
            fromSpan = _animationTargetSpan;
        else if (_lastWindowEndPhysical > _lastWindowStartPhysical)
            fromSpan = _lastWindowEndPhysical - _lastWindowStartPhysical;
        else
            return; // first layout after startup: nothing to animate from, snap

        if (Math.Abs(targetSpan - fromSpan) < 0.5)
        {
            // already at the target; make sure no stale timer keeps running
            StopWidthAnimation();
            return;
        }

        _animationFromSpan = fromSpan;
        _animationTargetSpan = targetSpan;
        _animationStartUtc = DateTime.UtcNow;
        _animationActive = true;
        _animationFramesApplied = 0;
        Logger.Info("Width animation started: {0:F0} -> {1:F0} px over {2:F0} ms", fromSpan, targetSpan, WidthAnimationDurationMs);
        Widget.SuppressMarqueeUpdates = true;
        EnsureWidthAnimationClock();
        windowEnd = windowStart + fromSpan;
    }

    private void EnsureWidthAnimationClock()
    {
        if (_widthAnimationTimer != null)
            return;

        // Send priority: our own layout work is queued at Background, which would starve the
        // animation ticks and make the whole run expire before a single frame applies
        _widthAnimationTimer = new DispatcherTimer(DispatcherPriority.Send)
        {
            Interval = TimeSpan.FromMilliseconds(WidthAnimationFrameMs)
        };
        _widthAnimationTimer.Tick += OnWidthAnimationFrame;
        _widthAnimationTimer.Start();
    }

    private void OnWidthAnimationFrame(object? sender, EventArgs e)
    {
        if (!_animationActive)
        {
            StopWidthAnimation();
            return;
        }

        if ((DateTime.UtcNow - _animationStartUtc).TotalMilliseconds >= WidthAnimationDurationMs)
        {
            // run finished: snap to the target with a final layout pass (also refreshes the
            // marquees suppressed during the run)
            _animationActive = false;
            StopWidthAnimation();
            Widget.SuppressMarqueeUpdates = false;
            Logger.Info("Width animation finished: {0} frames applied", _animationFramesApplied);
            UpdatePosition(immediate: true);
            return;
        }

        // immediate: animation frames must apply synchronously - deferring them through the
        // background dispatcher queue lets frames drain late or bunched, which reads as a snap
        _animationFramesApplied++;
        UpdatePosition(immediate: true);
    }

    private void StopWidthAnimation()
    {
        _animationActive = false;
        Widget.SuppressMarqueeUpdates = false;
        _widthAnimationTimer?.Stop();
        _widthAnimationTimer = null;
    }

    /// <summary>
    /// Repaints the visualizer bars with the current accent color scheme.
    /// </summary>
    public void RefreshVisualizer()
    {
        FluentFlyout.Controls.TaskbarVisualizerControl.RefreshVisualizerColors();
    }

    /// <summary>
    /// Locates the start edge of the system tray along the primary axis. Combines the UIA
    /// "SystemTrayIcon" lookup with the classic TrayNotifyWnd fallback.
    /// </summary>
    private bool TryFindTrayAnchor(IntPtr taskbarHandle, RECT taskbarRect, bool isMainTaskbarSelected, bool isVertical, bool allowAutomation, out double trayOffset)
    {
        trayOffset = 0;

        if (allowAutomation)
        {
            var (found, trayRect) = GetSystemTrayRect(taskbarHandle);
            if (found && (!isVertical || trayRect.Top >= taskbarRect.Top))
            {
                trayOffset = isVertical ? trayRect.Top - taskbarRect.Top : trayRect.Left - taskbarRect.Left;
                return true;
            }
        }

        if (_trayHandle == IntPtr.Zero || _lastSelectedMonitor != SettingsManager.Current.TaskbarWidgetSelectedMonitor)
            _trayHandle = FindWindowEx(taskbarHandle, IntPtr.Zero, "TrayNotifyWnd", null);

        if (_trayHandle != IntPtr.Zero)
        {
            GetWindowRect(_trayHandle, out RECT trayWndRect);
            double offset = isVertical ? trayWndRect.Top - taskbarRect.Top : trayWndRect.Left - taskbarRect.Left;

            // For vertical: validate the tray is in the lower half of the taskbar
            if (!isVertical || offset > (taskbarRect.Bottom - taskbarRect.Top) / 2)
            {
                trayOffset = offset;
                return true;
            }
        }

        return false;
    }

    // empty/partial UIA results are treated as unknown; the last known rect is kept briefly
    private (bool Found, Rect Cluster) GetTaskbarGroupRect(IntPtr taskbarHandle)
    {
        if (taskbarHandle == IntPtr.Zero)
            return (false, Rect.Empty);

        lock (_taskbarGroupRectLock)
        {
            // per-handle so monitor switches can't reuse rects from another taskbar
            if (_taskbarGroupRectHandle == taskbarHandle
                && !_taskbarGroupStale
                && DateTime.UtcNow - _taskbarGroupRectTime < TimeSpan.FromMilliseconds(1200))
            {
                return (_taskbarGroupFound, _taskbarGroupCluster);
            }

            if (_taskbarGroupQueryTask is { IsCompleted: false })
                return (_taskbarGroupFound, _taskbarGroupCluster);

            var handle = taskbarHandle;
            _taskbarGroupQueryTask = Task.Run(() =>
            {
                try
                {
                    var root = AutomationElement.FromHandle(handle);
                    if (root == null)
                        return;

                    var condition = new OrCondition(
                        new PropertyCondition(AutomationElement.ClassNameProperty, "Taskbar.TaskListButtonAutomationPeer"),
                        new PropertyCondition(AutomationElement.ClassNameProperty, "ToggleButton"));
                    var buttons = root.FindAll(TreeScope.Descendants, condition);

                    Rect cluster = Rect.Empty;
                    foreach (AutomationElement button in buttons)
                    {
                        try
                        {
                            // never measure our own widget's elements
                            if (button.Current.ProcessId == Environment.ProcessId)
                                continue;

                            Rect rect = button.Current.BoundingRectangle;
                            if (rect.IsEmpty || rect.Width <= 0)
                                continue;

                            cluster = cluster.IsEmpty ? rect : Rect.Union(cluster, rect);
                        }
                        catch (ElementNotAvailableException)
                        {
                            // a single button went stale - skip it
                        }
                    }

                    ApplyTaskbarGroupRect(handle, !cluster.IsEmpty, cluster);
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, "Failed to query taskbar element group rect.");
                }
            });

            return (_taskbarGroupFound, _taskbarGroupCluster);
        }
    }

    private void ApplyTaskbarGroupRect(IntPtr taskbarHandle, bool found, Rect cluster)
    {
        lock (_taskbarGroupRectLock)
        {
            if (taskbarHandle != _taskbarGroupRectHandle)
            {
                _taskbarGroupRectHandle = taskbarHandle;
                _taskbarGroupStale = false;
                _taskbarGroupFound = false;
                _taskbarGroupCluster = Rect.Empty;
            }

            if (!found && _taskbarGroupFound)
            {
                // empty result (Start menu open, explorer UIA busy): keep the last known rect
                // forever rather than falling back to "no obstacles" - full width here would
                // cover the user's app icons. Keep querying; the hook/poll reapplies on recovery.
                _taskbarGroupRectTime = DateTime.UtcNow;
                _taskbarGroupStale = false;
                return;
            }

            if (found)
            {
                bool changed = !_taskbarGroupFound || RectsDiffer(_taskbarGroupCluster, cluster);
                _taskbarGroupFound = true;
                _taskbarGroupCluster = cluster;
                _taskbarGroupStale = false;

                if (!_taskbarGroupLoggedOnce)
                {
                    _taskbarGroupLoggedOnce = true;
                    Logger.Info("Taskbar element group detected for adaptive width: {0}", cluster);
                }

                _taskbarGroupRectTime = DateTime.UtcNow;

                if (!changed)
                    return;
            }
            else
            {
                _taskbarGroupRectTime = DateTime.UtcNow;
            }
        }

        Dispatcher.BeginInvoke(() => UpdatePosition(), DispatcherPriority.Background);
    }

    private static bool RectsDiffer(Rect a, Rect b) =>
        Math.Abs(a.Left - b.Left) > 0.5 || Math.Abs(a.Right - b.Right) > 0.5;

    private void InstallWinEventHook()
    {
        // The taskbar handle is not known until the first UpdatePosition tick, which is later
        // than Window_Loaded, so this is also called from ResetTaskbarCachesIfHandleChanged.
        if (_lastTaskbarHandle == IntPtr.Zero || _isClosing)
            return;

        // scope the hook to explorer only; a global hook turns every app's UI activity
        // into work on our UI thread and makes the whole widget laggy
        uint explorerPid = NativeMethods.GetWindowThreadProcessId(_lastTaskbarHandle, IntPtr.Zero);
        if (explorerPid == 0)
            return;

        // already hooked to this explorer process - repeat calls are a cheap no-op
        if (_winEventHook != IntPtr.Zero && _winEventHookPid == explorerPid)
            return;

        // Explorer restarted (or the taskbar moved to another explorer process): the old hook
        // is scoped to a PID that no longer exists, so events would go silent forever.
        UninstallWinEventHook();

        _winEventProc = OnWinEvent;
        _winEventHook = NativeMethods.SetWinEventHook(EventObjectShow, EventObjectStateChange,
            IntPtr.Zero, _winEventProc, explorerPid, 0, WINEVENT_OUTOFCONTEXT);
        _winEventHookPid = explorerPid;
        Logger.Info("WinEvent hook installed for explorer pid {0}.", explorerPid);
    }

    private void UninstallWinEventHook()
    {
        if (_winEventHook == IntPtr.Zero)
            return;

        NativeMethods.UnhookWinEvent(_winEventHook);
        _winEventHook = IntPtr.Zero;
        _winEventProc = null;
        _winEventHookPid = 0;
    }

    private void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (_isClosing || hwnd == IntPtr.Zero || idObject != 0 /* OBJID_WINDOW */)
            return;

        IntPtr root = NativeMethods.GetAncestor(hwnd, GA_ROOT);
        if (root != GetSelectedTaskbarAnchor())
            return;

        if (eventType is EventObjectShow or EventObjectHide or EventObjectReorder or EventObjectStateChange)
        {
            MarkTaskbarGroupStale();
            return;
        }

        if (eventType == EventObjectLocationChange)
        {
            if ((DateTime.UtcNow - _lastGroupLocationEventUtc).TotalMilliseconds < 100)
                return;
            _lastGroupLocationEventUtc = DateTime.UtcNow;
            MarkTaskbarGroupStale();
        }
    }

    private IntPtr GetSelectedTaskbarAnchor()
    {
        return _lastTaskbarHandle;
    }

    // WinEvent bursts (a window animating open fires dozens of events) coalesce into one
    // scheduled update: the first event reacts immediately, the rest just re-mark stale
    private DateTime _lastBurstUpdateUtc = DateTime.MinValue;
    private bool _burstUpdateScheduled;
    private bool _wasTaskbarFull;

    private void MarkTaskbarGroupStale()
    {
        if (!SettingsManager.Current.TaskbarWidgetAdaptiveWidth)
            return;

        lock (_taskbarGroupRectLock)
            _taskbarGroupStale = true;

        DateTime now = DateTime.UtcNow;
        if ((now - _lastBurstUpdateUtc).TotalMilliseconds >= 50)
        {
            // outside a burst: react immediately
            _lastBurstUpdateUtc = now;
            Dispatcher.BeginInvoke(() => UpdatePosition(), DispatcherPriority.Background);
            return;
        }

        // inside a burst: schedule exactly one trailing pass if none is pending
        if (_burstUpdateScheduled)
            return;
        _burstUpdateScheduled = true;

        Dispatcher.BeginInvoke(() =>
        {
            _burstUpdateScheduled = false;
            _lastBurstUpdateUtc = DateTime.UtcNow;
            UpdatePosition();
        }, DispatcherPriority.Background);
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
            double taskbarCrossSize = (isVertical ? taskbarWidth : taskbarHeight) / dpiScale;
            bool isSmallTaskbar = taskbarCrossSize < SmallTaskbarDetectionThreshold;
            int containerWidth = taskbarWidth;
            int containerHeight = taskbarHeight;

            Widget.SetSmallTaskbarMode(isSmallTaskbar);
            TaskbarVisualizer.SetSmallTaskbarMode(isSmallTaskbar);

            // Following SetWindowPos will set the position relative to the parent window,
            // so those coordinates need to be converted.
            POINT containerPos = new() { X = taskbarRect.Left, Y = taskbarRect.Top };
            ScreenToClient(taskbarHandle, ref containerPos);

            // the free window and layout are computed every update so toggles apply instantly;
            // only the expensive window ops below are skipped when nothing changed
            double windowStartPhysical = 0, windowEndPhysical = -1;
            if (SettingsManager.Current.TaskbarWidgetAdaptiveWidth)
            {
                ComputeAdaptiveWindow(taskbarHandle, taskbarRect, dpiScale, isMainTaskbarSelected, isVertical, taskbarWidth, taskbarHeight, out windowStartPhysical, out windowEndPhysical);
                EaseWindowSpan(ref windowStartPhysical, ref windowEndPhysical);
            }

            var wRect = PositionWidget(taskbarHandle, taskbarRect, dpiScale, isMainTaskbarSelected, isVertical, windowStartPhysical, windowEndPhysical, out double visualizerWidth);
            var vRect = PositionVisualizer(taskbarHandle, taskbarRect, dpiScale, isMainTaskbarSelected, isVertical, visualizerWidth, windowStartPhysical, windowEndPhysical);

            bool unchanged = wRect == _lastWidgetRect && vRect == _lastVisualizerRect
                && taskbarRect.Equals(_lastTaskbarRect) && taskbarHandle == _lastTaskbarHandle
                && windowStartPhysical == _lastWindowStartPhysical && windowEndPhysical == _lastWindowEndPhysical;
            if (unchanged)
                return;

            // Apply using SetWindowPos (Bypassing WPF layout engine).
            // HWND_TOP keeps this child window at the top of the taskbar's child z-order:
            // Explorer's taskbar XAML content bridge (Windows.UI.Composition.DesktopWindowContentBridge)
            // spans the whole taskbar and otherwise ends up above this window, hiding the widget.
            // Do NOT pass SWP_NOZORDER here, it would turn hWndInsertAfter into a no-op.
            SetWindowPos(taskbarWindowHandle, HWND_TOP,
                     containerPos.X, containerPos.Y,
                     containerWidth, containerHeight,
                     SWP_NOACTIVATE | SWP_ASYNCWINDOWPOS | SWP_SHOWWINDOW);

            UpdateWindowRegion(taskbarWindowHandle, wRect, vRect);
            _lastTaskbarRect = taskbarRect;
            _lastWidgetRect = wRect;
            _lastVisualizerRect = vRect;
            _lastWindowStartPhysical = windowStartPhysical;
            _lastWindowEndPhysical = windowEndPhysical;

            _lastSelectedMonitor = SettingsManager.Current.TaskbarWidgetSelectedMonitor;
        }
        finally
        {
            _positionUpdateInProgress = false;
        }
    }

    private Rect PositionWidget(IntPtr taskbarHandle, RECT taskbarRect, double dpiScale, bool isMainTaskbarSelected, bool isVertical, double windowStartPhysical, double windowEndPhysical, out double visualizerWidth)
    {
        visualizerWidth = 0;

        if (!SettingsManager.Current.TaskbarWidgetEnabled)
            return Rect.Empty;

        Widget.SetVerticalMode(isVertical);

        int taskbarHeight = taskbarRect.Bottom - taskbarRect.Top;
        int taskbarWidth = taskbarRect.Right - taskbarRect.Left;

        // windowEndPhysical <= windowStartPhysical means unconstrained (adaptive off)
        bool adaptive = windowEndPhysical > windowStartPhysical;

        // Calculate widget size
        double availableSpan = adaptive
            ? Math.Max(windowEndPhysical - windowStartPhysical, 0) / (dpiScale * _scale)
            : double.PositiveInfinity;
        var (logicalWidth, logicalHeight, visualizerCanvasWidth) = Widget.CalculateSize(dpiScale, availableSpan);
        visualizerWidth = visualizerCanvasWidth;
        if (visualizerCanvasWidth > 0)
            TaskbarVisualizer.Width = visualizerCanvasWidth;

        int physicalWidth = (int)(logicalWidth * dpiScale * _scale);
        int physicalHeight = (int)(logicalHeight * dpiScale);

        // Apply orientation transform
        Widget.LayoutTransform = isVertical ? new System.Windows.Media.RotateTransform(90) : null;
        Widget.RenderTransform = System.Windows.Media.Transform.Identity;

        // On a vertical taskbar the widget is rotated 90°, so the axes flip:
        //   primarySize = taskbarHeight, positioning runs along Y
        //   crossSize   = taskbarWidth,  widget is centered along X
        //   physicalWidth  = visual extent along primary axis (logical width = visual height after rotation)
        //   physicalHeight = visual extent along cross axis   (logical height = visual width after rotation)
        int primarySize = isVertical ? taskbarHeight : taskbarWidth;
        int crossSize = isVertical ? taskbarWidth : taskbarHeight;

        // Center on the cross axis; both orientations use physicalHeight for the cross dimension
        int crossPos = (crossSize - physicalHeight) / 2;

        // Primary axis position (calculated per-case below)
        int primaryPos = 0;

        if (physicalWidth <= 0)
        {
            // the adaptive solver concluded nothing fits - hide the widget until space returns
            Canvas.SetLeft(Widget, 0);
            Canvas.SetTop(Widget, 0);
            Widget.Width = 0;
            Widget.Height = physicalHeight / dpiScale;
            return Rect.Empty;
        }

        switch (SettingsManager.Current.TaskbarWidgetPosition)
        {
            case 0: // near start (left for horizontal, top for vertical)
                primaryPos = 20;

                if (SettingsManager.Current.TaskbarVisualizerEnabled && SettingsManager.Current.TaskbarVisualizerPosition == 0)
                    primaryPos += (int)(TaskbarVisualizer.Width * dpiScale) + (int)VisualizerGapPx(dpiScale);

                if (!SettingsManager.Current.TaskbarWidgetPadding)
                    break;

                // automatic widget padding to the start
                try
                {
                    // find widget button in XAML
                    (bool found, Rect nativeWidgetRect) = GetTaskbarWidgetRect(taskbarHandle);

                    // Accept only if the native Widgets button is in the start half of the taskbar
                    bool inStartHalf = isVertical
                        ? nativeWidgetRect.Bottom < (taskbarRect.Top + taskbarRect.Bottom) / 2.0
                        : nativeWidgetRect.Right < (taskbarRect.Left + taskbarRect.Right) / 2.0;

                    if (found && inStartHalf)
                    {
                        // Convert absolute screen position to relative position within taskbar
                        primaryPos = isVertical
                            ? (int)(nativeWidgetRect.Bottom - taskbarRect.Top) + 2
                            : (int)(nativeWidgetRect.Right - taskbarRect.Left) + 2;
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
                if (adaptive)
                {
                    // center the widget + visualizer group inside the free window
                    double visPhysical = visualizerWidth > 0 ? visualizerWidth * dpiScale : 0;
                    double groupPhysical = physicalWidth + (visPhysical > 0 ? visPhysical + VisualizerGapPx(dpiScale) : 0);
                    double spanPhysical = Math.Max(windowEndPhysical - windowStartPhysical, 0);
                    double groupStart = windowStartPhysical + Math.Max((spanPhysical - groupPhysical) / 2.0, 0);
                    groupStart = Math.Min(groupStart, Math.Max(windowEndPhysical - groupPhysical, windowStartPhysical));
                    primaryPos = (int)(groupStart
                        + (visPhysical > 0 && SettingsManager.Current.TaskbarVisualizerPosition == 0 ? visPhysical + VisualizerGapPx(dpiScale) : 0));
                }
                else
                {
                    primaryPos = (primarySize - physicalWidth) / 2;

                    if (SettingsManager.Current.TaskbarVisualizerEnabled)
                        if (SettingsManager.Current.TaskbarVisualizerPosition == 0)
                            primaryPos += (int)(TaskbarVisualizer.Width * dpiScale) / 2 + (int)VisualizerGapPx(dpiScale);
                        else
                            primaryPos -= (int)(TaskbarVisualizer.Width * dpiScale) / 2 - (int)VisualizerGapPx(dpiScale);
                }
                break;

            case 2: // near end (right for horizontal, bottom for vertical)
                try
                {
                    if (SettingsManager.Current.TaskbarVisualizerEnabled && SettingsManager.Current.TaskbarVisualizerPosition == 1)
                        primaryPos -= (int)(TaskbarVisualizer.Width * dpiScale) - (int)VisualizerGapPx(dpiScale);

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

        // adaptive mode keeps manual padding from pushing the group under neighbouring elements
        if (adaptive && !isVertical && windowEndPhysical > windowStartPhysical)
        {
            bool visBefore = visualizerWidth > 0 && SettingsManager.Current.TaskbarVisualizerPosition == 0;
            double visPhysical = visBefore ? visualizerWidth * dpiScale + VisualizerGapPx(dpiScale) : 0;
            double minPos = windowStartPhysical + visPhysical;
            double maxPos = windowEndPhysical - physicalWidth - (visBefore ? 0 : visualizerWidth * dpiScale + VisualizerGapPx(dpiScale));
            primaryPos = (int)Math.Clamp(primaryPos, minPos, Math.Max(minPos, maxPos));
        }

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

    private Rect PositionVisualizer(IntPtr taskbarHandle, RECT taskbarRect, double dpiScale, bool isMainTaskbarSelected, bool isVertical, double visualizerWidth, double windowStartPhysical, double windowEndPhysical)
    {
        if (!SettingsManager.Current.TaskbarVisualizerEnabled || visualizerWidth <= 0)
        {
            TaskbarVisualizer.Visibility = Visibility.Collapsed;
            return Rect.Empty;
        }

        TaskbarVisualizer.Visibility = Visibility.Visible;
        TaskbarVisualizer.Width = visualizerWidth;

        // Rotate visualizer 90° on vertical taskbar so it fits the slim width
        TaskbarVisualizer.LayoutTransform = isVertical ? new System.Windows.Media.RotateTransform(90) : null;

        int taskbarHeight = taskbarRect.Bottom - taskbarRect.Top;
        int taskbarWidth = taskbarRect.Right - taskbarRect.Left;

        // TaskbarVisualizer.Height (40) is the cross-axis extent for both orientations:
        //   horizontal: actual height = 40, centered vertically (-1 to match native element alignment)
        //   vertical:   visual width after rotation = 40, centered horizontally
        int crossSize = isVertical ? taskbarWidth : taskbarHeight;
        int crossOffset = isVertical ? 0 : -1; // -1 aligns with native taskbar elements on horizontal
        int crossPos = (crossSize - (int)(TaskbarVisualizer.Height * dpiScale)) / 2 + crossOffset;

        // TaskbarVisualizer.Width (84) is the primary-axis extent for both orientations:
        //   horizontal: actual width = 84
        //   vertical:   visual height after rotation = 84
        // Position adjacent to the widget along the primary axis
        double widgetPrimaryStart = isVertical ? Canvas.GetTop(Widget) : Canvas.GetLeft(Widget);
        int primaryPos;

        // physical pixels; the solver already reserves this much span
        int gapPx = (int)VisualizerGapPx(dpiScale);

        switch (SettingsManager.Current.TaskbarVisualizerPosition)
        {
            case 0: // before widget (left for horizontal, above for vertical)
                primaryPos = (int)(widgetPrimaryStart * dpiScale) - (int)(TaskbarVisualizer.Width * dpiScale);

                // near-start placement already shifts itself by the gap
                if (SettingsManager.Current.TaskbarWidgetPosition != 0)
                    primaryPos -= gapPx;
                break;

            case 1: // after widget (right for horizontal, below for vertical)
                // Widget.Width holds the logical width; after 90° rotation its visual height = Widget.Width * dpiScale
                primaryPos = (int)(widgetPrimaryStart * dpiScale) + (int)(Widget.Width * dpiScale);

                // near-end placement already shifts itself by the gap
                if (SettingsManager.Current.TaskbarWidgetPosition != 2)
                    primaryPos += gapPx;
                break;

            default:
                primaryPos = 0;
                break;
        }

        // Set visualizer position within canvas
        // primaryPos → left (horizontal) or top (vertical); crossPos → top (horizontal) or left (vertical)
        Canvas.SetLeft(TaskbarVisualizer, (isVertical ? crossPos : primaryPos) / dpiScale);
        Canvas.SetTop(TaskbarVisualizer, (isVertical ? primaryPos : crossPos) / dpiScale);

        // keep the visualizer inside the free window
        if (windowEndPhysical > windowStartPhysical)
        {
            double visLeft = Canvas.GetLeft(TaskbarVisualizer) * dpiScale;
            double visWidth = TaskbarVisualizer.Width * dpiScale;
            double clampedLeft = Math.Clamp(visLeft, windowStartPhysical, Math.Max(windowStartPhysical, windowEndPhysical - visWidth));
            if (Math.Abs(clampedLeft - visLeft) > 0.5)
            {
                Canvas.SetLeft(TaskbarVisualizer, clampedLeft / dpiScale);
            }
        }

        // After 90° LayoutTransform the visual bounding rect has swapped dimensions
        double rectW = isVertical ? TaskbarVisualizer.Height * dpiScale : TaskbarVisualizer.Width * dpiScale;
        double rectH = isVertical ? TaskbarVisualizer.Width * dpiScale : TaskbarVisualizer.Height * dpiScale;
        return new Rect(Canvas.GetLeft(TaskbarVisualizer) * dpiScale, Canvas.GetTop(TaskbarVisualizer) * dpiScale, rectW, rectH);
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

        // Autohide - Widget hides when playback is paused
        _lastPlaybackStatus = playbackStatus;

        if ((SettingsManager.Current.TaskbarWidgetAutoHide))
        {
            if (playbackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
            {
                _autoHideTimer?.Stop();
                _autoHideTimer = null;

                Dispatcher.Invoke(() =>
                {
                    Visibility = Visibility.Visible;
                });
            }
            else
            {
                // Start delayed hide
                if (_autoHideTimer == null)
                {
                    var localTimer = new DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(750)
                    };

                    localTimer.Tick += (s, e) =>
                    {
                        localTimer.Stop();
                        if (_autoHideTimer == localTimer)
                            _autoHideTimer = null;

                        if (_lastPlaybackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                        {
                            Dispatcher.Invoke(() =>
                            {
                                Visibility = Visibility.Collapsed;
                            });
                        }
                    };

                    _autoHideTimer = localTimer;
                    localTimer.Start();
                }
            }
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

    public void RefreshAppVolumeTooltip()
    {
        Widget.RefreshAppVolumeTooltip();
    }

    private (bool, Rect) GetTaskbarXamlElementRect(IntPtr taskbarHandle, ref AutomationElement? elementCache, string elementName)
    {
        if (taskbarHandle == IntPtr.Zero)
            return (false, Rect.Empty);

        // UIA queries cost up to 500-1000ms each; during an animation serve rects from the
        // element cache instead, and never start a fresh search mid-run (full tree scan)
        if (_animationActive && elementCache != null)
        {
            try
            {
                var cachedElement = elementCache;
                Rect cachedRect = cachedElement.Current.BoundingRectangle;
                if (cachedRect != Rect.Empty)
                    return (true, cachedRect);
            }
            catch
            {
                // fall through to the normal (throttled) path below
            }
        }

        // unresolved element mid-animation: skip (full tree scan); next idle update re-queries
        if (_animationActive && elementCache == null)
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

    protected override void OnClosed(EventArgs e)
    {
        _isClosing = true;
        _timer.Stop();
        _autoHideTimer?.Stop();
        _autoHideTimer = null;
        StopWidthAnimation();
        UninstallWinEventHook();
        _widgetElement = null;
        _trayElement = null;
        _taskbarFrameElement = null;
        _pendingAutomationTasks.Clear();
        lock (_taskbarGroupRectLock)
        {
            _taskbarGroupRectHandle = IntPtr.Zero;
            _taskbarGroupFound = false;
            _taskbarGroupCluster = Rect.Empty;
            _taskbarGroupRectTime = DateTime.MinValue;
            _taskbarGroupStale = false;
            _taskbarGroupQueryTask = null;
        }
        base.OnClosed(e);
    }
}