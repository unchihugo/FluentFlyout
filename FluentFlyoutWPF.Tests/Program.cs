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

Run("Clamps stats font size to compact taskbar range", () =>
{
    AssertEqual(8d, SystemUsageStyleHelper.NormalizeFontSize(4));
    AssertEqual(12d, SystemUsageStyleHelper.NormalizeFontSize(12));
    AssertEqual(16d, SystemUsageStyleHelper.NormalizeFontSize(24));
    AssertEqual(SystemUsageStyleHelper.DefaultFontSize, SystemUsageStyleHelper.NormalizeFontSize(double.NaN));
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
