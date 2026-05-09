// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes;
using FluentFlyoutWPF.Classes.Utils;
using Microsoft.Toolkit.Uwp.Notifications;
using System.Windows;

namespace FluentFlyoutWPF;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        // log unhandled exceptions before crashing
        AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
        {
            NLog.LogManager.GetCurrentClassLogger().Error(args.ExceptionObject as Exception, "Unhandled exception occurred");
            NLog.LogManager.Flush(); // Ensure logs are written before application dies
        };

        // Register AUMID for toast notifications
        ToastNotificationManagerCompat.OnActivated += Notifications.HandleNotificationActivation;

        // Try to load Voicemeeter
        VoicemeeterLoader.Load();

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (VoicemeeterHelper.Instance != null)
        {
            VoicemeeterHelper.Instance.Dispose();
            VoicemeeterHelper.Instance = null;
        }

        base.OnExit(e);
    }
}