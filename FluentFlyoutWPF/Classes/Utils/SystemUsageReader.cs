// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes;
using System.Runtime.InteropServices;
using System.Windows.Media;

namespace FluentFlyoutWPF.Classes.Utils;

public readonly record struct SystemTimes(ulong Idle, ulong Kernel, ulong User);

public readonly record struct SystemUsageSnapshot(int CpuPercent, int RamPercent, bool HasCpuSample);

public readonly record struct SystemUsageDisplayText(string CpuText, string RamText);

public static class SystemUsageTextFormatter
{
    public static SystemUsageDisplayText FormatLines(SystemUsageSnapshot snapshot)
    {
        string cpuPercent = snapshot.HasCpuSample ? snapshot.CpuPercent.ToString() : "--";

        return new SystemUsageDisplayText(
            $"CPU {cpuPercent}%",
            $"RAM {snapshot.RamPercent}%");
    }
}

public static class SystemUsageStyleHelper
{
    public const string DefaultFontFamily = "Segoe UI Variable";
    public const string AutoColorValue = "Auto";
    public const double DefaultFontSize = 11;
    private const double MinFontSize = 8;
    private const double MaxFontSize = 16;

    public static double NormalizeFontSize(double fontSize)
    {
        if (double.IsNaN(fontSize))
            return DefaultFontSize;

        return Math.Clamp(fontSize, MinFontSize, MaxFontSize);
    }

    public static bool TryParseColor(string? value, out Color color)
    {
        color = default;

        if (string.IsNullOrWhiteSpace(value) || string.Equals(value.Trim(), AutoColorValue, StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            if (ColorConverter.ConvertFromString(value.Trim()) is Color parsedColor)
            {
                color = parsedColor;
                return true;
            }
        }
        catch (FormatException)
        {
        }
        catch (NotSupportedException)
        {
        }

        return false;
    }
}

public sealed class SystemUsageReader
{
    private SystemTimes? _previousCpuTimes;

    public SystemUsageSnapshot Read()
    {
        int ramPercent = TryReadMemoryUsagePercent(out int memoryLoad)
            ? memoryLoad
            : 0;

        if (!TryReadSystemTimes(out SystemTimes currentTimes))
            return new SystemUsageSnapshot(0, ramPercent, false);

        if (_previousCpuTimes is not { } previousTimes)
        {
            _previousCpuTimes = currentTimes;
            return new SystemUsageSnapshot(0, ramPercent, false);
        }

        int cpuPercent = CalculateCpuPercent(previousTimes, currentTimes);
        _previousCpuTimes = currentTimes;

        return new SystemUsageSnapshot(cpuPercent, ramPercent, true);
    }

    public static int CalculateCpuPercent(SystemTimes previous, SystemTimes current)
    {
        ulong idleDelta = Delta(previous.Idle, current.Idle);
        ulong kernelDelta = Delta(previous.Kernel, current.Kernel);
        ulong userDelta = Delta(previous.User, current.User);
        ulong totalDelta = kernelDelta + userDelta;

        if (totalDelta == 0)
            return 0;

        ulong busyDelta = totalDelta > idleDelta ? totalDelta - idleDelta : 0;
        double cpuPercent = busyDelta * 100d / totalDelta;

        return Math.Clamp((int)Math.Round(cpuPercent, MidpointRounding.AwayFromZero), 0, 100);
    }

    private static bool TryReadSystemTimes(out SystemTimes times)
    {
        times = default;

        if (!NativeMethods.GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
            return false;

        times = new SystemTimes(ToUInt64(idleTime), ToUInt64(kernelTime), ToUInt64(userTime));
        return true;
    }

    private static bool TryReadMemoryUsagePercent(out int memoryLoad)
    {
        NativeMethods.MEMORYSTATUSEX status = new()
        {
            dwLength = (uint)Marshal.SizeOf<NativeMethods.MEMORYSTATUSEX>()
        };

        if (!NativeMethods.GlobalMemoryStatusEx(ref status))
        {
            memoryLoad = 0;
            return false;
        }

        memoryLoad = Math.Clamp((int)status.dwMemoryLoad, 0, 100);
        return true;
    }

    private static ulong ToUInt64(NativeMethods.FILETIME fileTime)
    {
        return ((ulong)fileTime.dwHighDateTime << 32) | fileTime.dwLowDateTime;
    }

    private static ulong Delta(ulong previous, ulong current)
    {
        return current >= previous ? current - previous : 0;
    }
}
