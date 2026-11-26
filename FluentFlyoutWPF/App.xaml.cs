// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes;
using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF.Classes.Services;
using Microsoft.Toolkit.Uwp.Notifications;
using System.Windows;
using System.Windows.Threading;
using Wpf.Ui.Appearance;

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
            NLog.LogManager.GetCurrentClassLogger().Fatal(args.ExceptionObject as Exception, "Unhandled exception occurred");
            NLog.LogManager.Flush(); // Ensure logs are written before application dies
        };

        // Register AUMID for toast notifications
        ToastNotificationManagerCompat.OnActivated += Notifications.HandleNotificationActivation;

        // WPF-UI still updates the window backdrop after raising Changed.
        ApplicationThemeManager.Changed += (_, _) =>
            Dispatcher.InvokeAsync(RefreshAcrylic, DispatcherPriority.Background);

        await ExperimentsService.GetExperimentsAsync();

        base.OnStartup(e);
    }

    private static void RefreshAcrylic()
    {
        try
        {
            WindowBlurHelper.AdjustBlurOpacityForAllWindows(SettingsManager.Current.AcrylicBlurOpacity);
        }
        catch (Exception ex)
        {
            NLog.LogManager.GetCurrentClassLogger().Error(ex, "Failed to refresh acrylic after applying the theme");
        }
    }
}