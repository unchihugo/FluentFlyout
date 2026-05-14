// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes;
using LibreHardwareMonitor.Hardware;
using System.Runtime.InteropServices;
using System.Windows.Media;

namespace FluentFlyoutWPF.Classes.Utils;

public readonly record struct SystemTimes(ulong Idle, ulong Kernel, ulong User);

public readonly record struct SystemUsageSnapshot(int CpuPercent, int RamPercent, bool HasCpuSample, int? CpuTemperatureCelsius = null);

public readonly record struct SystemUsageDisplayText(string CpuText, string RamText);

public static class SystemUsageTextFormatter
{
    public static SystemUsageDisplayText FormatLines(SystemUsageSnapshot snapshot, bool showCpuTemperature = false)
    {
        string cpuPercent = snapshot.HasCpuSample ? snapshot.CpuPercent.ToString() : "--";
        string cpuText = $"CPU {cpuPercent}%";

        if (showCpuTemperature)
        {
            string cpuTemperature = snapshot.CpuTemperatureCelsius.HasValue
                ? snapshot.CpuTemperatureCelsius.Value.ToString()
                : "--";
            cpuText += $" · {cpuTemperature}°C";
        }

        return new SystemUsageDisplayText(
            cpuText,
            $"RAM {snapshot.RamPercent}%");
    }
}

public readonly record struct CpuTemperatureSensorReading(string Name, double? ValueCelsius);

public static class CpuTemperatureSelector
{
    private static readonly string[] PreferredSensorNames =
    [
        "CPU Package",
        "Tctl/Tdie",
        "Package",
        "CPU Die",
        "Core Max"
    ];

    public static int? SelectCpuTemperatureCelsius(IEnumerable<CpuTemperatureSensorReading> sensors)
    {
        CpuTemperatureSensorReading[] validSensors = sensors
            .Where(sensor => IsValidTemperature(sensor.ValueCelsius))
            .ToArray();

        foreach (string preferredName in PreferredSensorNames)
        {
            CpuTemperatureSensorReading? match = validSensors.FirstOrDefault(sensor =>
                (sensor.Name ?? string.Empty).Contains(preferredName, StringComparison.OrdinalIgnoreCase));

            if (match is { ValueCelsius: { } temperature })
                return RoundTemperature(temperature);
        }

        double? maxTemperature = validSensors
            .Select(sensor => sensor.ValueCelsius)
            .Max();

        return maxTemperature.HasValue
            ? RoundTemperature(maxTemperature.Value)
            : null;
    }

    private static bool IsValidTemperature(double? temperature)
    {
        return temperature is >= 1 and <= 125
            && !double.IsNaN(temperature.Value)
            && !double.IsInfinity(temperature.Value);
    }

    private static int RoundTemperature(double temperature)
    {
        return (int)Math.Round(temperature, MidpointRounding.AwayFromZero);
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

public sealed class SystemUsageReader : IDisposable
{
    private SystemTimes? _previousCpuTimes;
    private readonly CpuTemperatureReader _cpuTemperatureReader = new();

    public SystemUsageSnapshot Read(bool includeCpuTemperature = true)
    {
        int ramPercent = TryReadMemoryUsagePercent(out int memoryLoad)
            ? memoryLoad
            : 0;
        int? cpuTemperature = includeCpuTemperature
            ? _cpuTemperatureReader.ReadCelsius()
            : null;

        if (!TryReadSystemTimes(out SystemTimes currentTimes))
            return new SystemUsageSnapshot(0, ramPercent, false, cpuTemperature);

        if (_previousCpuTimes is not { } previousTimes)
        {
            _previousCpuTimes = currentTimes;
            return new SystemUsageSnapshot(0, ramPercent, false, cpuTemperature);
        }

        int cpuPercent = CalculateCpuPercent(previousTimes, currentTimes);
        _previousCpuTimes = currentTimes;

        return new SystemUsageSnapshot(cpuPercent, ramPercent, true, cpuTemperature);
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

    public void Dispose()
    {
        _cpuTemperatureReader.Dispose();
    }
}

public sealed class CpuTemperatureReader : IDisposable
{
    private Computer? _computer;
    private bool _isOpened;
    private bool _isUnavailable;

    public int? ReadCelsius()
    {
        if (_isUnavailable)
            return null;

        try
        {
            EnsureOpen();

            if (_computer is null)
                return null;

            return CpuTemperatureSelector.SelectCpuTemperatureCelsius(ReadSensors(_computer.Hardware));
        }
        catch
        {
            _isUnavailable = true;
            return null;
        }
    }

    private void EnsureOpen()
    {
        if (_isOpened)
            return;

        _computer = new Computer { IsCpuEnabled = true };
        _computer.Open();
        _isOpened = true;
    }

    private static IEnumerable<CpuTemperatureSensorReading> ReadSensors(IEnumerable<IHardware> hardwareItems)
    {
        foreach (IHardware hardware in hardwareItems)
        {
            if (hardware.HardwareType != HardwareType.Cpu)
                continue;

            hardware.Update();

            foreach (IHardware subHardware in hardware.SubHardware)
            {
                subHardware.Update();
            }

            foreach (CpuTemperatureSensorReading sensor in ReadHardwareSensors(hardware))
            {
                yield return sensor;
            }
        }
    }

    private static IEnumerable<CpuTemperatureSensorReading> ReadHardwareSensors(IHardware hardware)
    {
        foreach (ISensor sensor in hardware.Sensors)
        {
            if (sensor.SensorType == SensorType.Temperature)
                yield return new CpuTemperatureSensorReading(sensor.Name, sensor.Value);
        }

        foreach (IHardware subHardware in hardware.SubHardware)
        {
            foreach (ISensor sensor in subHardware.Sensors)
            {
                if (sensor.SensorType == SensorType.Temperature)
                    yield return new CpuTemperatureSensorReading(sensor.Name, sensor.Value);
            }
        }
    }

    public void Dispose()
    {
        _computer?.Close();
        _computer = null;
        _isOpened = false;
        _isUnavailable = true;
    }
}
