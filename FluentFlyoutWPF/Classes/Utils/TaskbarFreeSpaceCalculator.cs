// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

namespace FluentFlyout.Classes.Utils;

/// <summary>
/// Taskbar anchor rectangles and layout flags needed to compute the free span
/// available to the taskbar widget. All values are in taskbar-relative pixels.
/// </summary>
public sealed record TaskbarAnchors
{
    /// <summary>Taskbar size along the primary axis (width, or height when vertical).</summary>
    public required double PrimarySize { get; init; }

    /// <summary>Taskbar midpoint along the primary axis.</summary>
    public required double PrimaryMid { get; init; }

    /// <summary>Whether the "Widgets" taskbar button was found.</summary>
    public bool WidgetsFound { get; init; }

    public double WidgetBtnStart { get; init; }

    public double WidgetBtnEnd { get; init; }

    public bool WidgetBtnInStartHalf { get; init; }

    public bool WidgetBtnInEndHalf { get; init; }

    /// <summary>System tray start edge, when the tray was located.</summary>
    public bool TrayFound { get; init; }

    public double TrayStart { get; init; }

    /// <summary>Centered element group (fixed buttons + app icons) edges, when found.</summary>
    public bool GroupFound { get; init; }

    public double GroupStart { get; init; }

    public double GroupEnd { get; init; }

    /// <summary>Automatic taskbar padding is enabled.</summary>
    public bool WidgetPaddingEnabled { get; init; }

    public bool IsVertical { get; init; }

    public bool IsMainTaskbarSelected { get; init; }
}

/// <summary>
/// Result of the free-span calculation before hide-when-full and hysteresis are applied.
/// </summary>
public sealed record TaskbarSpanResult
{
    public required double WindowStart { get; init; }

    public required double WindowEnd { get; init; }

    /// <summary>
    /// True when no usable gap exists, i.e. <c>WindowEnd &lt; WindowStart</c>.
    /// </summary>
    public required bool Collapsed { get; init; }
}

public static class TaskbarFreeSpaceCalculator
{
    /// <summary>
    /// Computes the free span, in taskbar-relative pixels, that the taskbar widget may
    /// occupy for the configured widget position.
    /// </summary>
    public static TaskbarSpanResult Calculate(TaskbarAnchors a, int widgetPosition)
    {
        double windowStart;
        double windowEnd;

        switch (widgetPosition)
        {
            case 1: // center: use the free gap that leaves the most room around the icon cluster
                {
                    windowStart = 20;
                    windowEnd = a.TrayFound ? a.TrayStart - 4 : a.PrimarySize - 20;

                    if (a.GroupFound && a.GroupStart < windowEnd && a.GroupEnd > windowStart)
                    {
                        // the group covers the middle - fall back to the larger side gap
                        double leftGap = a.GroupStart - 4 - windowStart;
                        double rightGap = windowEnd - (a.GroupEnd + 4);
                        if (leftGap >= rightGap && leftGap > 0)
                            windowEnd = Math.Min(windowEnd, a.GroupStart - 4);
                        else if (rightGap > 0)
                            windowStart = Math.Max(windowStart, a.GroupEnd + 4);
                    }
                    break;
                }

            case 2: // near end: keep the legacy anchor, but never grow into the app icon cluster
                {
                    double rightLimit;
                    if (!a.IsVertical && a.WidgetPaddingEnabled && a.WidgetBtnInEndHalf)
                        rightLimit = a.WidgetBtnStart - 1;
                    else if (a.TrayFound)
                        rightLimit = a.TrayStart - (a.IsVertical ? 2 : a.IsMainTaskbarSelected ? 6 : 1);
                    else
                        rightLimit = a.PrimarySize - 20;

                    windowEnd = rightLimit;
                    windowStart = 20;
                    if (a.GroupFound && a.GroupEnd + 4 < rightLimit)
                        windowStart = a.GroupEnd + 4;
                    break;
                }

            default: // near start
                {
                    windowStart = a.WidgetPaddingEnabled && a.WidgetBtnInStartHalf ? a.WidgetBtnEnd + 2 : 20;
                    windowEnd = a.TrayFound ? a.TrayStart - 4 : a.PrimarySize - 20;

                    // keep clear of the centered group (fixed buttons + icons) whenever a gap exists
                    if (a.GroupFound && a.GroupStart - 4 > windowStart)
                        windowEnd = Math.Min(windowEnd, a.GroupStart - 4);
                    break;
                }
        }

        return new TaskbarSpanResult
        {
            WindowStart = windowStart,
            WindowEnd = windowEnd,
            Collapsed = windowEnd < windowStart,
        };
    }
}