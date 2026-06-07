using Microsoft.Win32;
using System.IO;
using System.Runtime.InteropServices;

namespace FluentFlyoutWPF.Classes.Utils;

public class VoicemeeterLoader
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LoadLibrary(string lpFileName);

    private const string DefaultPath = @"C:\Program Files (x86)\VB\Voicemeeter\VoicemeeterRemote64.dll";

    public static bool IsLoaded { get; set; }
    public static string? LoadedPath { get; private set; }

    public static bool Load()
    {
        if (IsLoaded) return true;

        string? path = FindDll();

        if (path == null)
        {
            System.Diagnostics.Debug.WriteLine("Voicemeeter DLL not found");
            IsLoaded = false;
            return false;
        }

        IntPtr handle = LoadLibrary(path);

        if (handle == IntPtr.Zero)
        {
            int error = Marshal.GetLastWin32Error();
            System.Diagnostics.Debug.WriteLine($"Failed to load Voicemeeter library: {error}");

            IsLoaded = false;
            return false;
        }

        IsLoaded = true;
        LoadedPath = path;

        return true;
    }

    private static string? FindDll()
    {
        string? installPath = GetInstallPath();

        // Try getting the path from Registry
        if (!string.IsNullOrEmpty(installPath))
        {
            string dll = Path.Combine(installPath, "VoicemeeterRemote64.dll");

            if (File.Exists(dll))
            {
                return dll;
            }
        }

        // If path can't be got from Registry, try the default instalation path
        if (File.Exists(DefaultPath))
        {
            return DefaultPath;
        }

        // If default path is also invalid, try looking in the local directory of the app
        string localPath = Path.Combine(AppContext.BaseDirectory, "VoicemeeterRemote64.dll");

        return File.Exists(localPath) ? localPath : null;
    }

    public static string? GetInstallPath()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\VB-Audio\Voicemeeter");
            return key?.GetValue("InstallPath") as string;
        }
        catch
        {
            return null;
        }
    }
}