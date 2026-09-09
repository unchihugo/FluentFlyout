// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes;
using FluentFlyoutWPF.Classes.Services;
using Microsoft.Toolkit.Uwp.Notifications;
using System.Windows;

namespace FluentFlyoutWPF;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // log unhandled exceptions before crashing
        AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
        {
            NLog.LogManager.GetCurrentClassLogger().Fatal(args.ExceptionObject as Exception, "Unhandled exception occurred");
            NLog.LogManager.Flush(); // Ensure logs are written before application dies
        };

        // Register AUMID for toast notifications
        ToastNotificationManagerCompat.OnActivated += Notifications.HandleNotificationActivation;

        // StartupUri creates MainWindow from base.OnStartup. Do not put remote
        // work in front of it: the tray/flyout must exist even when the network
        // is unavailable.
        base.OnStartup(e);

        // App and MainWindow receive the same shared request. This warm-up is
        // intentionally fire-and-forget and fault-tolerant.
        _ = LoadExperimentsInBackgroundAsync();
    }

    private static async Task LoadExperimentsInBackgroundAsync()
    {
        try
        {
            await ExperimentsService.GetExperimentsAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            NLog.LogManager.GetCurrentClassLogger().Error(ex, "Background experiments initialization failed");
        }
    }
}