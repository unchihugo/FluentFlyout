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
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    protected override async void OnStartup(StartupEventArgs e)
    {
        // log unhandled exceptions before crashing
        AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
        {
            NLog.LogManager.GetCurrentClassLogger().Fatal(args.ExceptionObject as Exception, "Unhandled exception occurred");
            NLog.LogManager.Flush(); // Ensure logs are written before application dies
        };

        // Register before asynchronous startup work so Restart Manager can restart the app after an update.
        int restartRegistrationResult = NativeMethods.RegisterApplicationRestart(
            null,
            NativeMethods.RESTART_NO_CRASH | NativeMethods.RESTART_NO_HANG);
        if (restartRegistrationResult == NativeMethods.S_OK)
        {
            Logger.Info("Registered for application restart after an update");
        }
        else
        {
            Logger.Warn($"Failed to register for application restart. HRESULT: 0x{restartRegistrationResult:X8}");
        }

        // Register AUMID for toast notifications
        ToastNotificationManagerCompat.OnActivated += Notifications.HandleNotificationActivation;

        await ExperimentsService.GetExperimentsAsync();

        base.OnStartup(e);
    }
}