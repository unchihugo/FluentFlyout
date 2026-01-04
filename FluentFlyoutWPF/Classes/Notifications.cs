using Microsoft.Toolkit.Uwp.Notifications;
using NLog;
using System.Runtime.InteropServices;
using System.Windows;

namespace FluentFlyout.Classes;

[ComVisible(true)]
[Guid("79086E7F-0D65-4507-82B6-85F2288930D5")]
internal static class Notifications
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Handle toast notification activation
    /// </summary>
    public static void HandleNotificationActivation(ToastNotificationActivatedEventArgsCompat toastArgs)
    {
        try
        {
            // Obtain the arguments from the notification
            ToastArguments args = ToastArguments.Parse(toastArgs.Argument);

            // Check if the user clicked the "View Changes" button
            if (args.TryGetValue("action", out string action) && action == "viewChanges")
            {
                // Open the changelog URL in the default web browser
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://fluentflyout.com/changelog/",
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to handle notification activation");
        }
    }

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
            return;

            //new ToastContentBuilder()
            //    .AddText("Welcome to FluentFlyout!")
            //    .AddText("Thank you for installing FluentFlyout. You can access the settings at any time by clicking the tray icon.")
            //    .Show();

            //return;
        }

        if (currentVersion == "debug")
        {
            // when on debug build
            return;

            //new ToastContentBuilder()
            //    .AddText("FluentFlyout Debug Build")
            //    .AddText("You are running a debug build.")
            //    .Show();

            //return;
        }

        if (lastKnownVersion != currentVersion)
        {
            try
            {
                // updated app version
                new ToastContentBuilder()
                    .AddText(Application.Current.FindResource("UpdateToastTitle").ToString())
                    .AddText(string.Format(Application.Current.FindResource("UpdateToastMessage").ToString(), currentVersion))
                    .AddButton(new ToastButton()
                        .SetContent(Application.Current.FindResource("UpdateToastButton").ToString())
                        .AddArgument("action", "viewChanges")
                        .SetBackgroundActivation())
                    .Show();

                Logger.Info($"Displayed update notification for {currentVersion}.");

                return;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to show update notification");
                return;
            }
        }
    }
}