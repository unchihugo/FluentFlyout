// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF.Classes.Clients;
using NLog;

namespace FluentFlyoutWPF.Classes.Services;

public static class TelemetryService
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private const string ApiEndpoint = "events";

    public static async Task SendTelemetryEventAsync(string eventName, string? experimentId = null)
    {
        if (!SettingsManager.Current.AnonymousTelemetryAllowed) return;

        try
        {
            string appVersion = (SettingsManager.Current.LastKnownVersion ?? "unknown")
                + "-"
                + (SettingsManager.Current.IsStoreVersion ? "store" : "github");

            var telemetryData = new
            {
                eventName,
                experimentId = experimentId ?? string.Empty,
                variant = ExperimentsService.CheckUuidInExperiment(experimentId ?? string.Empty),
                userId = SettingsManager.Current.Uuid,
                sessionId = SettingsManager.Current.SessionId,
                appVersion,
            };

            await FluentFlyoutApiClient.PostAsJsonAsync(ApiEndpoint, telemetryData);
        }
        catch (TaskCanceledException ex)
        {
            Logger.Info(ex, "Failed to send telemetry event - request timed out.");
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Failed to send telemetry event: {0}", eventName);
        }
    }
}