// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyoutWPF.Classes.Utils;

Run("Calculates CPU usage from system time deltas", () =>
{
    SystemTimes previous = new(Idle: 100, Kernel: 200, User: 300);
    SystemTimes current = new(Idle: 150, Kernel: 350, User: 400);

    int percent = SystemUsageReader.CalculateCpuPercent(previous, current);

    AssertEqual(80, percent);
});

Run("Returns zero CPU usage when system time does not advance", () =>
{
    SystemTimes previous = new(Idle: 100, Kernel: 200, User: 300);
    SystemTimes current = new(Idle: 100, Kernel: 200, User: 300);

    int percent = SystemUsageReader.CalculateCpuPercent(previous, current);

    AssertEqual(0, percent);
});

Run("Formats system usage as two compact taskbar lines", () =>
{
    SystemUsageSnapshot snapshot = new(CpuPercent: 18, RamPercent: 64, HasCpuSample: true);

    var text = SystemUsageTextFormatter.FormatLines(snapshot);

    AssertEqual("CPU 18%", text.CpuText);
    AssertEqual("RAM 64%", text.RamText);
});

Run("Uses CPU placeholder before first sample", () =>
{
    SystemUsageSnapshot snapshot = new(CpuPercent: 0, RamPercent: 64, HasCpuSample: false);

    var text = SystemUsageTextFormatter.FormatLines(snapshot);

    AssertEqual("CPU --%", text.CpuText);
    AssertEqual("RAM 64%", text.RamText);
});

Run("Formats CPU temperature on the CPU stats line", () =>
{
    SystemUsageSnapshot snapshot = new(CpuPercent: 18, RamPercent: 64, HasCpuSample: true, CpuTemperatureCelsius: 72);

    var text = SystemUsageTextFormatter.FormatLines(snapshot, showCpuTemperature: true);

    AssertEqual("CPU 18% · 72°C", text.CpuText);
    AssertEqual("RAM 64%", text.RamText);
});

Run("Formats CPU temperature placeholder before sensor sample", () =>
{
    SystemUsageSnapshot snapshot = new(CpuPercent: 18, RamPercent: 64, HasCpuSample: true);

    var text = SystemUsageTextFormatter.FormatLines(snapshot, showCpuTemperature: true);

    AssertEqual("CPU 18% · --°C", text.CpuText);
    AssertEqual("RAM 64%", text.RamText);
});

Run("Keeps CPU temperature hidden when disabled", () =>
{
    SystemUsageSnapshot snapshot = new(CpuPercent: 18, RamPercent: 64, HasCpuSample: true, CpuTemperatureCelsius: 72);

    var text = SystemUsageTextFormatter.FormatLines(snapshot, showCpuTemperature: false);

    AssertEqual("CPU 18%", text.CpuText);
});

Run("Formats CPU placeholder with temperature when CPU sample is pending", () =>
{
    SystemUsageSnapshot snapshot = new(CpuPercent: 0, RamPercent: 64, HasCpuSample: false, CpuTemperatureCelsius: 72);

    var text = SystemUsageTextFormatter.FormatLines(snapshot, showCpuTemperature: true);

    AssertEqual("CPU --% · 72°C", text.CpuText);
});

Run("Selects CPU package temperature before other valid sensors", () =>
{
    CpuTemperatureSensorReading[] sensors =
    [
        new("Core Max", 81),
        new("CPU Package", 72),
        new("CPU Core #1", 64)
    ];

    AssertEqual(72, CpuTemperatureSelector.SelectCpuTemperatureCelsius(sensors));
});

Run("Falls back to max valid CPU temperature sensor", () =>
{
    CpuTemperatureSensorReading[] sensors =
    [
        new("CPU Core #1", 64),
        new("CPU Core #2", 71)
    ];

    AssertEqual(71, CpuTemperatureSelector.SelectCpuTemperatureCelsius(sensors));
});

Run("Ignores invalid CPU temperature sensor values", () =>
{
    CpuTemperatureSensorReading[] sensors =
    [
        new("CPU Package", double.NaN),
        new("Tctl/Tdie", 0),
        new("CPU Die", -4),
        new("Core Max", 126)
    ];

    AssertEqual<int?>(null, CpuTemperatureSelector.SelectCpuTemperatureCelsius(sensors));
});

Run("Selects MSI Afterburner CPU temperature by source id", () =>
{
    MsiAfterburnerMonitoringEntry[] entries =
    [
        new("GPU1 temperature", 51, 0x00000000),
        new("CPU temperature", 70, 0x00000080),
        new("CPU usage", 6, 0x00000090)
    ];

    AssertEqual(70, MsiAfterburnerTemperatureSelector.SelectCpuTemperatureCelsius(entries));
});

Run("Ignores invalid MSI Afterburner CPU temperature values", () =>
{
    MsiAfterburnerMonitoringEntry[] entries =
    [
        new("CPU temperature", float.MaxValue, 0x00000080),
        new("CPU temperature", float.NaN, 0x00000080),
        new("CPU temperature", 0, 0x00000080),
        new("CPU temperature", 126, 0x00000080)
    ];

    AssertEqual<int?>(null, MsiAfterburnerTemperatureSelector.SelectCpuTemperatureCelsius(entries));
});

Run("Does not treat GPU temperature as CPU temperature", () =>
{
    MsiAfterburnerMonitoringEntry[] entries =
    [
        new("GPU1 temperature", 51, 0x00000000)
    ];

    AssertEqual<int?>(null, MsiAfterburnerTemperatureSelector.SelectCpuTemperatureCelsius(entries));
});

Run("Clamps stats font size to compact taskbar range", () =>
{
    AssertEqual(13d, SystemUsageStyleHelper.DefaultFontSize);
    AssertEqual(8d, SystemUsageStyleHelper.NormalizeFontSize(4));
    AssertEqual(12d, SystemUsageStyleHelper.NormalizeFontSize(12));
    AssertEqual(16d, SystemUsageStyleHelper.NormalizeFontSize(24));
    AssertEqual(SystemUsageStyleHelper.DefaultFontSize, SystemUsageStyleHelper.NormalizeFontSize(double.NaN));
});

Run("Selects CPU temperature foreground colors by threshold", () =>
{
    AssertEqual(false, SystemUsageStyleHelper.TryGetCpuTemperatureColor(null, out _));

    AssertEqual(true, SystemUsageStyleHelper.TryGetCpuTemperatureColor(59, out var white));
    AssertColor(0xFF, 0xFF, 0xFF, 0xFF, white);

    AssertEqual(true, SystemUsageStyleHelper.TryGetCpuTemperatureColor(60, out var yellowStart));
    AssertColor(0xFF, 0xFF, 0xD1, 0x66, yellowStart);

    AssertEqual(true, SystemUsageStyleHelper.TryGetCpuTemperatureColor(79, out var yellowEnd));
    AssertColor(0xFF, 0xFF, 0xD1, 0x66, yellowEnd);

    AssertEqual(true, SystemUsageStyleHelper.TryGetCpuTemperatureColor(80, out var orangeStart));
    AssertColor(0xFF, 0xFF, 0x9F, 0x1C, orangeStart);

    AssertEqual(true, SystemUsageStyleHelper.TryGetCpuTemperatureColor(90, out var orangeEnd));
    AssertColor(0xFF, 0xFF, 0x9F, 0x1C, orangeEnd);

    AssertEqual(true, SystemUsageStyleHelper.TryGetCpuTemperatureColor(91, out var red));
    AssertColor(0xFF, 0xFF, 0x45, 0x3A, red);
});

Run("Parses custom stats color and treats Auto as theme color", () =>
{
    bool parsed = SystemUsageStyleHelper.TryParseColor("#FF00AA66", out var color);

    AssertEqual(true, parsed);
    AssertEqual(0xFF, color.A);
    AssertEqual(0x00, color.R);
    AssertEqual(0xAA, color.G);
    AssertEqual(0x66, color.B);
    AssertEqual(false, SystemUsageStyleHelper.TryParseColor("Auto", out _));
});

static void Run(string name, Action test)
{
    try
    {
        test();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"FAIL {name}: {ex.Message}");
        Environment.ExitCode = 1;
    }
}

static void AssertEqual<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"expected {expected}, got {actual}");
}

static void AssertColor(byte expectedA, byte expectedR, byte expectedG, byte expectedB, System.Windows.Media.Color actual)
{
    AssertEqual(expectedA, actual.A);
    AssertEqual(expectedR, actual.R);
    AssertEqual(expectedG, actual.G);
    AssertEqual(expectedB, actual.B);
}
