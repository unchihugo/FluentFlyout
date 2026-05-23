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

Run("Normalizes taskbar widget font families", () =>
{
    AssertEqual(SystemUsageStyleHelper.DefaultMediaFontFamily, SystemUsageStyleHelper.NormalizeFontFamily(null, SystemUsageStyleHelper.DefaultMediaFontFamily));
    AssertEqual(SystemUsageStyleHelper.DefaultMediaFontFamily, SystemUsageStyleHelper.NormalizeFontFamily("  ", SystemUsageStyleHelper.DefaultMediaFontFamily));
    AssertEqual("Cascadia Mono", SystemUsageStyleHelper.NormalizeFontFamily("  Cascadia Mono  ", SystemUsageStyleHelper.DefaultMediaFontFamily));
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

Run("Uses 9router CLI secret when creating local auth token", () =>
{
    string tempDirectory = Path.Combine(Path.GetTempPath(), "fluentflyout-9router-token-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Path.Combine(tempDirectory, "auth"));
    File.WriteAllText(Path.Combine(tempDirectory, "machine-id"), "machine-123");
    File.WriteAllText(Path.Combine(tempDirectory, "auth", "cli-secret"), "secret-456");

    using HttpRequestMessage request = new(HttpMethod.Get, "http://localhost:20128/api/providers");

    bool applied = CodexUsageCliTokenProvider.TryApplyHeader(request, tempDirectory);

    AssertEqual(true, applied);
    AssertEqual("35499f2df791a8a0", string.Join("", request.Headers.GetValues(CodexUsageCliTokenProvider.HeaderName)));
    Directory.Delete(tempDirectory, recursive: true);
});

Run("Parses Codex provider metadata when quota endpoint is unavailable", () =>
{
    string json = """
    {
      "id": "codex-1",
      "provider": "codex",
      "isActive": true,
      "testStatus": "active",
      "priority": 2,
      "providerSpecificData": {
        "chatgptPlanType": "plus"
      },
      "modelLock_gpt-5.5": "2026-05-23T17:30:00Z",
      "lastError": "[429]: The usage limit has been reached",
      "lastErrorAt": "2026-05-23T16:30:00Z"
    }
    """;

    CodexUsageSnapshot snapshot = CodexUsageApiPayloadParser.ParseUsagePayload(json, "codex-1");

    AssertEqual(true, snapshot.HasData);
    AssertEqual("plus", snapshot.Plan);
    AssertEqual(true, snapshot.LimitReached);
    AssertEqual(1, snapshot.Session?.Used);
    AssertEqual(1, snapshot.Session?.Total);
    AssertEqual(0, snapshot.Session?.Remaining);
    AssertEqual(new DateTime(2026, 5, 23, 17, 30, 0, DateTimeKind.Utc), snapshot.Session?.ResetAtUtc);
});

Run("Clamps taskbar bounds to selected monitor", () =>
{
    System.Windows.Rect taskbarRect = new(0, 1040, 3840, 40);
    System.Windows.Rect selectedMonitorRect = new(1920, 0, 1920, 1080);

    System.Windows.Rect clamped = TaskbarWindowGeometryHelper.ClampTaskbarRectToMonitor(taskbarRect, selectedMonitorRect);

    AssertEqual(1920d, clamped.Left);
    AssertEqual(1040d, clamped.Top);
    AssertEqual(1920d, clamped.Width);
    AssertEqual(40d, clamped.Height);
});

Run("Maps All monitors taskbar selection to every monitor", () =>
{
    int[] monitorIndexes = TaskbarWidgetMonitorSelection.GetTargetMonitorIndexes(
        TaskbarWidgetMonitorSelection.AllMonitorsValue,
        3);

    AssertSequenceEqual([0, 1, 2], monitorIndexes);
});

Run("Keeps existing taskbar monitor indexes stable when All monitors is available", () =>
{
    int[] monitorIndexes = TaskbarWidgetMonitorSelection.GetTargetMonitorIndexes(0, 3);

    AssertSequenceEqual([0], monitorIndexes);
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

static void AssertSequenceEqual<T>(IReadOnlyList<T> expected, IReadOnlyList<T> actual)
{
    if (!expected.SequenceEqual(actual))
        throw new InvalidOperationException($"expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}]");
}

static void AssertColor(byte expectedA, byte expectedR, byte expectedG, byte expectedB, System.Windows.Media.Color actual)
{
    AssertEqual(expectedA, actual.A);
    AssertEqual(expectedR, actual.R);
    AssertEqual(expectedG, actual.G);
    AssertEqual(expectedB, actual.B);
}
