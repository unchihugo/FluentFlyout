using Microsoft.Toolkit.Uwp.Notifications;
using System;
using System.Collections.Generic;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluentFlyout.Classes;

internal static class Notifications
{
    /// <summary>
    /// Show a Windows notification if the application is run for the first time or has been updated.
    /// </summary>
    /// <param name="lastKnownVersion"></param>
    /// <param name="currentVersion"></param>
    public static void ShowFirstOrUpdateNotification(string lastKnownVersion, string currentVersion)
    {
        if (String.IsNullOrEmpty(lastKnownVersion))
        {
            // first run
            new ToastContentBuilder()
                .AddAppLogoOverride(new Uri("ms-appx:///Assets/FluentFlyoutLogo.png"), ToastGenericAppLogoCrop.None)
                .AddText("Welcome to FluentFlyout!")
                .AddText("Thank you for installing FluentFlyout. You can access the settings at any time by clicking the tray icon.")
                .Show();

            return;
        }

        if (currentVersion == "debug")
        {
            new ToastContentBuilder()
                .AddAppLogoOverride(new Uri("file:///" + Directory.GetCurrentDirectory() + "/Resources/FluentFlyout2.ico"), ToastGenericAppLogoCrop.None)
                .AddText("FluentFlyout Debug Build")
                .AddText("You are running a debug build of FluentFlyout.")
                .Show();

            return;
        }

        if (lastKnownVersion != currentVersion)
        {
            // updated app
            new ToastContentBuilder()
                .AddAppLogoOverride(new Uri("ms-appx:///Assets/FluentFlyoutLogo.png"), ToastGenericAppLogoCrop.None)
                .AddText("FluentFlyout has been updated!")
                .AddText($"You are now running version {currentVersion}. Check out the changelog in the GitHub repository for more details.")
                .Show();

            return;
        }
    }
}
