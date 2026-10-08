// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Runtime.InteropServices;
using static FluentFlyout.Classes.NativeMethods;

namespace FluentFlyoutWPF.Classes;

internal static class LocalInputDetector
{
    public static bool HadInputWithin(int milliseconds)
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info))
            return false;

        uint elapsed = unchecked((uint)Environment.TickCount - info.dwTime);
        return elapsed < milliseconds;
    }
}
