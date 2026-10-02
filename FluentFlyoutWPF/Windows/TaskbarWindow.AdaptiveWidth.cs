// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes.Settings;
using FluentFlyout.Classes.Utils;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using static FluentFlyout.Classes.NativeMethods;

namespace FluentFlyout.Windows;

public partial class TaskbarWindow
{
    // Scanned off the UI thread; a synchronous subtree walk can block for seconds
    private readonly Lock _taskbarGroupRectLock = new();
    private bool _taskbarGroupFound;
    private Rect _taskbarGroupCluster = Rect.Empty;
    private IntPtr _taskbarGroupRectHandle;
    private DateTime _taskbarGroupRectTime = DateTime.MinValue;
    private Task? _taskbarGroupQueryTask;
    private bool _taskbarGroupLoggedOnce;

    // Adaptive width polls faster than the historical idle interval so resizes feel immediate.
    private const double IdlePollIntervalMs = 1500;
    private const double AdaptivePollIntervalMs = 200;

    // Pinning an app resizes the cluster without moving any window rect, hence this TTL
    private const double GroupRectMaxAgeMs = 1500;

    // hysteresis band (physical px) that absorbs jitter between layout tiers
    private const double SpanHysteresis = 4;
    private double _debouncedSpanPhysical = -1;
    private bool _wasTaskbarFull;

    // Held while the group rect refreshes so a taskbar change does not reposition from stale geometry
    private double _lastWindowStartPhysical = -1;
    private double _lastWindowEndPhysical = -1;

    // Invalidated when the taskbar reports a layout change
    private bool _trayAnchorValid;
    private bool _trayAnchorFound;
    private double _trayAnchorOffset;

    // Memoised native Widgets button rect; its bounds read is a blocking cross-process call.
    private bool _widgetButtonValid;
    private bool _widgetButtonFound;
    private Rect _widgetButtonRect;

    // Keyed by element name: one shared slot let a miss leak between lookups and feed a retry loop
    private const double TaskbarElementBoundsCacheMs = 1000;
    private readonly Dictionary<string, (DateTime Time, bool Found, Rect Rect)> _elementBoundsCache = [];

    // Without this a miss re-walks the whole subtree on the next pass, blocking the UI thread
    private const double TaskbarElementMissingCacheMs = 5000;
    private readonly Dictionary<string, DateTime> _elementMissingUntil = [];

    private bool _geometryProbeValid;
    private int _probeTaskbarWidth;
    private int _probeTaskbarHeight;
    private int _probeTrayLeft;
    private int _probeTrayRight;
    private int _probeTrayTop;
    private int _probeTrayBottom;

    // Last applied geometry, used to skip redundant WPF layout work
    private bool _lastGeometryValid;
    private int _lastPrimaryPos;
    private int _lastCrossPos;
    private int _lastPhysicalWidth;
    private int _lastPhysicalHeight;
    private bool _lastIsVertical;
    private double _lastVisualizerWidth;
    private Rect _lastWidgetRect;
    private bool _skippedReposition;

    // Set by the solver, read back by PositionVisualizer
    private double _adaptiveVisualizerWidth;

    private (bool Found, Rect Cluster) GetTaskbarGroupRect(IntPtr taskbarHandle, bool geometryChanged)
    {
        if (taskbarHandle == IntPtr.Zero)
            return (false, Rect.Empty);

        lock (_taskbarGroupRectLock)
        {
            // The walk is the expensive part, so it runs on a probe hit plus the pin/unpin TTL
            bool ttlExpired = DateTime.UtcNow - _taskbarGroupRectTime
                >= TimeSpan.FromMilliseconds(GroupRectMaxAgeMs);

            if (_taskbarGroupRectHandle == taskbarHandle
                && _taskbarGroupFound
                && !geometryChanged
                && !ttlExpired)
            {
                return (_taskbarGroupFound, _taskbarGroupCluster);
            }

            // a query is already in flight - serve the previous value rather than stacking queries
            if (_taskbarGroupQueryTask is { IsCompleted: false })
                return (_taskbarGroupFound, _taskbarGroupCluster);

            _taskbarGroupRectTime = DateTime.UtcNow;

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
                finally
                {
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
                _taskbarGroupFound = false;
                _taskbarGroupCluster = Rect.Empty;
            }

            if (!found && _taskbarGroupFound)
            {
                // Empty result (Start menu open, Explorer UIA busy): keep the last known rect,
                // since "no obstacles" would draw the widget over the app icons. Leaving the
                // timestamp untouched keeps the query retrying.
                return;
            }

            if (found)
            {
                bool changed = !_taskbarGroupFound || RectsDiffer(_taskbarGroupCluster, cluster);
                _taskbarGroupFound = true;
                _taskbarGroupCluster = cluster;

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

    private bool TryFindTrayAnchor(IntPtr taskbarHandle, RECT taskbarRect, bool isMainTaskbarSelected, bool isVertical, bool allowAutomation, bool geometryChanged, out double trayOffset)
    {
        // Resolving the tray costs tens of ms, enough to stutter the visualizer, so memoise it
        if (_trayAnchorValid && !geometryChanged)
        {
            trayOffset = _trayAnchorOffset;
            return _trayAnchorFound;
        }

        trayOffset = 0;
        if (allowAutomation)
        {
            var (found, trayRect) = GetSystemTrayRect(taskbarHandle);
            if (found && (!isVertical || trayRect.Top >= taskbarRect.Top))
            {
                trayOffset = isVertical ? trayRect.Top - taskbarRect.Top : trayRect.Left - taskbarRect.Left;
                _trayAnchorValid = true;
                _trayAnchorFound = true;
                _trayAnchorOffset = trayOffset;
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
                _trayAnchorValid = true;
                _trayAnchorFound = true;
                _trayAnchorOffset = trayOffset;
                return true;
            }
        }

        _trayAnchorValid = true;
        _trayAnchorFound = false;
        return false;
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
        _elementBoundsCache.Clear();
        _elementMissingUntil.Clear();

        // the handle changed, so the group rect belongs to a different taskbar
        lock (_taskbarGroupRectLock)
        {
            _taskbarGroupRectHandle = IntPtr.Zero;
            _taskbarGroupFound = false;
            _taskbarGroupCluster = Rect.Empty;
            _taskbarGroupRectTime = DateTime.MinValue;
            _taskbarGroupQueryTask = null;
        }

        _debouncedSpanPhysical = -1;
        // the held window belongs to the old taskbar, so it must not leak to the new one
        _lastWindowStartPhysical = -1;
        _lastWindowEndPhysical = -1;
        _trayAnchorValid = false;
        _widgetButtonValid = false;
    }

    // Cheap enough to run every pass; cannot see pin/unpin, which the group-rect TTL covers
    private bool TaskbarGeometryChanged(IntPtr taskbarHandle, RECT taskbarRect)
    {
        IntPtr trayWindow = FindWindowEx(taskbarHandle, IntPtr.Zero, "TrayNotifyWnd", null);

        int trayLeft = int.MinValue, trayRight = int.MinValue;
        int trayTop = int.MinValue, trayBottom = int.MinValue;
        if (trayWindow != IntPtr.Zero && GetWindowRect(trayWindow, out RECT trayRect))
        {
            trayLeft = trayRect.Left;
            trayRight = trayRect.Right;
            trayTop = trayRect.Top;
            trayBottom = trayRect.Bottom;
        }

        bool changed = !_geometryProbeValid
            || _probeTaskbarWidth != taskbarRect.Right - taskbarRect.Left
            || _probeTaskbarHeight != taskbarRect.Bottom - taskbarRect.Top
            || _probeTrayLeft != trayLeft
            || _probeTrayRight != trayRight
            || _probeTrayTop != trayTop
            || _probeTrayBottom != trayBottom;

        if (changed)
        {
            _geometryProbeValid = true;
            _probeTaskbarWidth = taskbarRect.Right - taskbarRect.Left;
            _probeTaskbarHeight = taskbarRect.Bottom - taskbarRect.Top;
            _probeTrayLeft = trayLeft;
            _probeTrayRight = trayRight;
            _probeTrayTop = trayTop;
            _probeTrayBottom = trayBottom;
        }

        return changed;
    }

    // Hide-when-full and hysteresis live here rather than in the calculator, being stateful
    private (double Start, double End, bool IsMeasured) ComputeAdaptiveSpanPhysical(IntPtr taskbarHandle, RECT taskbarRect, bool isMainTaskbarSelected, bool isVertical)
    {
        int primarySize = isVertical
            ? taskbarRect.Bottom - taskbarRect.Top
            : taskbarRect.Right - taskbarRect.Left;

        double primaryMid = isVertical
            ? (taskbarRect.Top + taskbarRect.Bottom) / 2.0
            : (taskbarRect.Left + taskbarRect.Right) / 2.0;

        // The cheap probe gates every expensive UI Automation lookup below.
        bool geometryChanged = TaskbarGeometryChanged(taskbarHandle, taskbarRect);

        // Only queried when automatic padding is on, as in the legacy path
        bool widgetsFound;
        Rect widgetsRect;
        if (!SettingsManager.Current.TaskbarWidgetPadding)
        {
            widgetsFound = false;
            widgetsRect = Rect.Empty;
        }
        else if (_widgetButtonValid && !geometryChanged)
        {
            widgetsFound = _widgetButtonFound;
            widgetsRect = _widgetButtonRect;
        }
        else
        {
            (widgetsFound, widgetsRect) = GetTaskbarWidgetRect(taskbarHandle);
            _widgetButtonValid = true;
            _widgetButtonFound = widgetsFound;
            _widgetButtonRect = widgetsRect;
        }

        double widgetBtnStart = 0, widgetBtnEnd = 0;
        bool widgetBtnInStartHalf = false, widgetBtnInEndHalf = false;
        if (widgetsFound)
        {
            widgetBtnStart = isVertical ? widgetsRect.Top - taskbarRect.Top : widgetsRect.Left - taskbarRect.Left;
            widgetBtnEnd = isVertical ? widgetsRect.Bottom - taskbarRect.Top : widgetsRect.Right - taskbarRect.Left;
            widgetBtnInStartHalf = isVertical ? widgetsRect.Bottom < primaryMid : widgetsRect.Right < primaryMid;
            widgetBtnInEndHalf = !isVertical && widgetsRect.Left > primaryMid;
        }

        bool trayFound = TryFindTrayAnchor(taskbarHandle, taskbarRect, isMainTaskbarSelected, isVertical, allowAutomation: true, geometryChanged, out double trayStart)
            && trayStart > 0 && trayStart < primarySize;

        // Centered element group; shifts sideways on center-aligned taskbars.
        var (groupRectFound, groupRect) = GetTaskbarGroupRect(taskbarHandle, geometryChanged);
        bool groupFound = false;
        double groupStart = 0, groupEnd = 0;
        if (groupRectFound)
        {
            groupStart = isVertical ? groupRect.Top - taskbarRect.Top : groupRect.Left - taskbarRect.Left;
            groupEnd = isVertical ? groupRect.Bottom - taskbarRect.Top : groupRect.Right - taskbarRect.Left;
            groupFound = groupEnd > groupStart && groupStart < primarySize && groupEnd > 0;
        }

        var span = TaskbarFreeSpaceCalculator.Calculate(new TaskbarAnchors
        {
            PrimarySize = primarySize,
            PrimaryMid = primaryMid,
            WidgetsFound = widgetsFound,
            WidgetBtnStart = widgetBtnStart,
            WidgetBtnEnd = widgetBtnEnd,
            WidgetBtnInStartHalf = widgetBtnInStartHalf,
            WidgetBtnInEndHalf = widgetBtnInEndHalf,
            TrayFound = trayFound,
            TrayStart = trayStart,
            GroupFound = groupFound,
            GroupStart = groupStart,
            GroupEnd = groupEnd,
            WidgetPaddingEnabled = SettingsManager.Current.TaskbarWidgetPadding,
            IsVertical = isVertical,
            IsMainTaskbarSelected = isMainTaskbarSelected,
        }, SettingsManager.Current.TaskbarWidgetPosition);

        double windowStart = span.WindowStart;
        double windowEnd = span.WindowEnd;

        if (span.Collapsed)
        {
            if (!_wasTaskbarFull)
                Logger.Info($"Taskbar window collapsed: start={windowStart:F1} end={windowEnd:F1} " +
                    $"groupEnd={groupEnd:F1} trayStart={(trayFound ? trayStart.ToString("F1") : "not found")} " +
                    $"taskbarWidth={primarySize}");
            _wasTaskbarFull = true;

            if (SettingsManager.Current.TaskbarWidgetHideWhenFull)
            {
                // 100% full: collapse to zero so the solver hides the widget
                windowStart = 0;
                windowEnd = 0;
                _debouncedSpanPhysical = 0;
                RememberWindow(windowStart, windowEnd);
                return (windowStart, windowEnd, true);
            }

            // Stale or bogus rects: report unmeasured rather than anchoring to the whole taskbar
            windowStart = 0;
            windowEnd = primarySize;
            return (windowStart, windowEnd, false);
        }

        _wasTaskbarFull = false;

        // Spans inside the hysteresis band reuse the last width, to avoid tier flicker
        double measured = windowEnd - windowStart;
        if (_debouncedSpanPhysical >= 0 && Math.Abs(measured - _debouncedSpanPhysical) > 0.25
            && Math.Abs(measured - _debouncedSpanPhysical) <= SpanHysteresis)
        {
            windowEnd = windowStart + _debouncedSpanPhysical;
            RememberWindow(windowStart, windowEnd);
            return (windowStart, windowEnd, true);
        }

        _debouncedSpanPhysical = measured;
        RememberWindow(windowStart, windowEnd);
        return (windowStart, windowEnd, true);
    }

    private void RememberWindow(double start, double end)
    {
        _lastWindowStartPhysical = start;
        _lastWindowEndPhysical = end;
    }
}