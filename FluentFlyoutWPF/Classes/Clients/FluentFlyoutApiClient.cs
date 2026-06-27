// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes.Settings;
using System.Net.Http;

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

        Client.DefaultRequestHeaders.UserAgent.ParseAdd($"FluentFlyout/{SettingsManager.Current.LastKnownVersion}");
    }
}
