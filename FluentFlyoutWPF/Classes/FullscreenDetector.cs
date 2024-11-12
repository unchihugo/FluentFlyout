using FluentFlyout.Properties;
using System.Runtime.InteropServices;


namespace FluentFlyoutWPF.Classes;

internal class FullscreenDetector
{
    /// <summary>
    /// Represents the different states of user notification returned by the Windows Shell API.
    /// </summary>
    public enum QUERY_USER_NOTIFICATION_STATE
    {
        QUNS_NOT_PRESENT = 1,
        QUNS_BUSY = 2,
        QUNS_RUNNING_D3D_FULL_SCREEN = 3,
        QUNS_PRESENTATION_MODE = 4,
        QUNS_ACCEPTS_NOTIFICATIONS = 5,
        QUNS_QUIET_TIME = 6,
        QUNS_APP = 7
    }

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out QUERY_USER_NOTIFICATION_STATE pquns);

    /// <summary>
    /// Checks if a DirectX exclusive fullscreen application or game is currently running.
    /// </summary>
    /// <returns>
    /// true if a fullscreen DirectX application is running;
    /// false if no fullscreen application is detected, DisableIfFullscreen setting is false, or if the check fails
    /// </returns>
    public static bool IsFullscreenApplicationRunning()
    {
        if (!Settings.Default.DisableIfFullscreen) return false;
        try
        {
            QUERY_USER_NOTIFICATION_STATE state;
            int result = SHQueryUserNotificationState(out state);

            if (result != 0) // 0 means SUCCESS
            {
                throw new Exception($"SHQueryUserNotificationState failed with error code: {result}");
            }

            return state == QUERY_USER_NOTIFICATION_STATE.QUNS_RUNNING_D3D_FULL_SCREEN;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error detecting fullscreen state: {ex.Message}");
            return false;
        }
    }
}