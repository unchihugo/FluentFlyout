// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes.Settings;
using NLog;
using System.Net.Http;
using System.Net.Http.Json;

namespace FluentFlyoutWPF.Classes.Services;

public static class TelemetryService
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private const string ApiEndpoint = "https://fluentflyout.com/api/events";

    public static async Task SendTelemetryEventAsync(string eventName, string? experimentId = null)
    {
        if (!SettingsManager.Current.AnonymousTelemetryAllowed) return;

        try
        {
            using HttpClient client = new() { Timeout = TimeSpan.FromSeconds(2) };

            var telemetryData = new
            {
                eventName,
                experimentId = experimentId ?? string.Empty,
                variant = ExperimentsService.CheckUuidInExperiment(experimentId ?? string.Empty),
                userId = SettingsManager.Current.Uuid,
                sessionId = SettingsManager.Current.SessionId
            };

            await client.PostAsJsonAsync(ApiEndpoint, telemetryData);
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Failed to send telemetry event: {0}", eventName);
        }
    }
}
