// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes.Settings;
using System.Net.Http;
using System.Net.Http.Json;

namespace FluentFlyoutWPF.Classes.Clients;

public sealed class FluentFlyoutApiClient
{
    public static readonly HttpClient Client;

    static FluentFlyoutApiClient()
    {
        Client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(2),
            BaseAddress = new Uri("https://fluentflyout.com/api/")
        };

        UpdateUserAgent();
    }

    public static Task PostAsJsonAsync<T>(string requestUri, T content)
    {
        UpdateUserAgent();
        return Client.PostAsJsonAsync(requestUri, content);
    }

    private static void UpdateUserAgent()
    {
        string appVersion = SettingsManager.Current.LastKnownVersion;
        string normalizedVersion = string.IsNullOrWhiteSpace(appVersion) ? "unknown" : appVersion;

        Client.DefaultRequestHeaders.UserAgent.Clear();
        Client.DefaultRequestHeaders.UserAgent.ParseAdd($"FluentFlyout/{normalizedVersion}");
    }
}