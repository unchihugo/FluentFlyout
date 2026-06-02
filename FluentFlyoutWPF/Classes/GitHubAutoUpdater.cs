// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

#if GITHUB_RELEASE

using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF.ViewModels;
using NLog;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Windows;

namespace FluentFlyout.Classes;

public static class GitHubAutoUpdater
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private static Timer? _periodicTimer;
    private static bool _isRunning;
    private static readonly Lock _lock = new();
    private const string GitHubOwner = "unchihugo";
    private const string GitHubRepo = "FluentFlyout";
    private static string ApiUrl => $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";

    private static string UpdateDirectory => Path.Combine(
        Path.GetTempPath(),
        "FluentFlyout",
        "Updates"
    );

    //checking interval is 4 hours 
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(4);

    public static async Task StartAsync()
    {
        if (!SettingsManager.Current.AutoUpdateEnabled)
        {
            Logger.Info("GitHubAutoUpdater: Auto-update is disabled in settings.");
            return;
        }

        if (SettingsManager.Current.IsStoreVersion)
        {
            Logger.Info("GitHubAutoUpdater: Store version detected, skipping GitHub auto-updater.");
            return;
        }

        lock (_lock)
        {
            if (_isRunning)
            {
                Logger.Info("GitHubAutoUpdater: Already running.");
                return;
            }
            _isRunning = true;
        }

        Logger.Info("GitHubAutoUpdater: Starting background auto-updater.");

        //requires user agent for github api
        if (HttpClient.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            HttpClient.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("FluentFlyout", GetCurrentVersion()));
        }

        //delays for app to startup
        await Task.Delay(TimeSpan.FromSeconds(15));

        await CheckAndUpdateAsync();

        //periodic timer
        _periodicTimer = new Timer(
            async _ => await CheckAndUpdateAsync(),
            null,
            CheckInterval,
            CheckInterval
        );
    }

    //stops auto updater
    public static void Stop()
    {
        lock (_lock)
        {
            _periodicTimer?.Dispose();
            _periodicTimer = null;
            _isRunning = false;
        }

        Logger.Info("GitHubAutoUpdater: Stopped.");
    }

    //checks cycle
    private static async Task CheckAndUpdateAsync()
    {
        if (!SettingsManager.Current.AutoUpdateEnabled)
        {
            Logger.Info("GitHubAutoUpdater: Auto-update disabled, skipping check.");
            return;
        }

        try
        {
            Logger.Info("GitHubAutoUpdater: Checking for updates...");

            var releaseInfo = await GetLatestReleaseAsync();
            if (releaseInfo == null)
            {
                Logger.Info("GitHubAutoUpdater: Could not fetch release info.");
                return;
            }

            string currentVersion = GetCurrentVersion();
            if (currentVersion == "debug")
            {
                Logger.Info("GitHubAutoUpdater: Debug build, skipping auto-update.");
                return;
            }

            if (!IsNewerVersion(currentVersion, releaseInfo.TagName))
            {
                Logger.Info($"GitHubAutoUpdater: Up to date. Current: {currentVersion}, Latest: {releaseInfo.TagName}");
                return;
            }

            Logger.Info($"GitHubAutoUpdater: New version available: {releaseInfo.TagName} (current: {currentVersion})");

            //updates ui
            Application.Current.Dispatcher.Invoke(() =>
            {
                UpdateState.Current.IsUpdateAvailable = true;
                UpdateState.Current.NewestVersion = releaseInfo.TagName;
                UpdateState.Current.UpdateUrl = releaseInfo.HtmlUrl;
                UpdateState.Current.LastUpdateCheck = DateTime.Now;
            });

            //finds the current running msix build/ architecture
            var msixAsset = FindMsixAsset(releaseInfo);
            if (msixAsset == null)
            {
                Logger.Warn("GitHubAutoUpdater: No suitable MSIX asset found in the release.");
                return;
            }

            //downloads the new msix file
            string downloadedPath = await DownloadAssetAsync(msixAsset, releaseInfo.TagName);
            if (string.IsNullOrEmpty(downloadedPath))
            {
                Logger.Warn("GitHubAutoUpdater: Download failed.");
                return;
            }

            //installation of patch
            bool installed = await InstallMsixAsync(downloadedPath);
            if (installed)
            {
                Logger.Info($"GitHubAutoUpdater: Successfully installed update {releaseInfo.TagName}");

                Application.Current.Dispatcher.Invoke(() =>
                {
                    UpdateState.Current.IsUpdateReadyToInstall = true;
                    UpdateState.Current.IsDownloadingUpdate = false;
                    UpdateState.Current.DownloadProgress = 100;
                });

                //notification after installation is done.
                Notifications.ShowUpdateInstalledNotification(releaseInfo.TagName);
            }
            else
            {
                Logger.Warn("GitHubAutoUpdater: MSIX installation failed.");

                Application.Current.Dispatcher.Invoke(() =>
                {
                    UpdateState.Current.IsDownloadingUpdate = false;
                    UpdateState.Current.DownloadProgress = 0;
                });
            }
            CleanupDownloads();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "GitHubAutoUpdater: Error during auto-update check");

            Application.Current.Dispatcher.Invoke(() =>
            {
                UpdateState.Current.IsDownloadingUpdate = false;
                UpdateState.Current.DownloadProgress = 0;
            });
        }
    }

    //gets latest release from github api 
    private static async Task<GitHubRelease?> GetLatestReleaseAsync()
    {
        try
        {
            var response = await HttpClient.GetStringAsync(ApiUrl);
            var json = JsonDocument.Parse(response);
            var root = json.RootElement;

            var release = new GitHubRelease
            {
                TagName = root.GetProperty("tag_name").GetString() ?? string.Empty,
                HtmlUrl = root.GetProperty("html_url").GetString() ?? string.Empty,
                Assets = []
            };

            if (root.TryGetProperty("assets", out var assetsElement))
            {
                foreach (var asset in assetsElement.EnumerateArray())
                {
                    release.Assets.Add(new GitHubAsset
                    {
                        Name = asset.GetProperty("name").GetString() ?? string.Empty,
                        BrowserDownloadUrl = asset.GetProperty("browser_download_url").GetString() ?? string.Empty,
                        Size = asset.GetProperty("size").GetInt64()
                    });
                }
            }

            return release;
        }
        catch (HttpRequestException ex)
        {
            Logger.Info($"GitHubAutoUpdater: Network error fetching release: {ex.Message}");
            return null;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "GitHubAutoUpdater: Error parsing release info");
            return null;
        }
    }

    //check the current .msix file for the current architecture i.e. (x64 or arm64)
    private static GitHubAsset? FindMsixAsset(GitHubRelease release)
    {
        string arch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.X64 => "x64",
            System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
            _ => "x64"
        };

        foreach (var asset in release.Assets)
        {
            string name = asset.Name.ToLowerInvariant();
            if (name.EndsWith(".msix", StringComparison.OrdinalIgnoreCase) &&
                name.Contains(arch, StringComparison.OrdinalIgnoreCase))
            {
                return asset;
            }
        }

        foreach (var asset in release.Assets)
        {
            if (asset.Name.EndsWith(".msix", StringComparison.OrdinalIgnoreCase))
            {
                return asset;
            }
        }

        //check for the msixbundle file.
        foreach (var asset in release.Assets)
        {
            if (asset.Name.EndsWith(".msixbundle", StringComparison.OrdinalIgnoreCase))
            {
                return asset;
            }
        }

        return null;
    }

    //downloading msix to temp folder with progress bar and skips if already downloaded
    private static async Task<string> DownloadAssetAsync(GitHubAsset asset, string version)
    {
        try
        {
            string targetDir = Path.Combine(UpdateDirectory, version.TrimStart('v'));
            Directory.CreateDirectory(targetDir);

            string targetPath = Path.Combine(targetDir, asset.Name);

            //skips if the file is already downloaded
            if (File.Exists(targetPath))
            {
                var existingFile = new FileInfo(targetPath);
                if (existingFile.Length == asset.Size)
                {
                    Logger.Info($"GitHubAutoUpdater: Update already downloaded: {targetPath}");
                    return targetPath;
                }
            }

            Logger.Info($"GitHubAutoUpdater: Downloading {asset.Name} ({asset.Size / 1024.0 / 1024.0:F1} MB)...");

            Application.Current.Dispatcher.Invoke(() =>
            {
                UpdateState.Current.IsDownloadingUpdate = true;
                UpdateState.Current.DownloadProgress = 0;
            });

            using var response = await HttpClient.GetAsync(asset.BrowserDownloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            long totalBytes = response.Content.Headers.ContentLength ?? asset.Size;
            long downloadedBytes = 0;

            using var contentStream = await response.Content.ReadAsStreamAsync();
            using var fileStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

            byte[] buffer = new byte[8192];
            int bytesRead;
            int lastReportedProgress = 0;

            while ((bytesRead = await contentStream.ReadAsync(buffer)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                downloadedBytes += bytesRead;

                int progress = totalBytes > 0 ? (int)(downloadedBytes * 100 / totalBytes) : 0;
                if (progress != lastReportedProgress)
                {
                    lastReportedProgress = progress;
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        UpdateState.Current.DownloadProgress = progress;
                    });
                }
            }

            Logger.Info($"GitHubAutoUpdater: Download complete: {targetPath}");
            return targetPath;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"GitHubAutoUpdater: Error downloading {asset.Name}");
            return string.Empty;
        }
    }

   //installs msix via powershell cmd
    private static async Task<bool> InstallMsixAsync(string msixPath)
    {
        try
        {
            Logger.Info($"GitHubAutoUpdater: Installing MSIX: {msixPath}");

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"Add-AppxPackage -Path '{msixPath}' -ForceApplicationShutdown\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                Logger.Error("GitHubAutoUpdater: Failed to start PowerShell process.");
                return false;
            }

            string output = await process.StandardOutput.ReadToEndAsync();
            string error = await process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            if (process.ExitCode == 0)
            {
                Logger.Info($"GitHubAutoUpdater: MSIX installed successfully. Output: {output}");
                return true;
            }
            else
            {
                Logger.Error($"GitHubAutoUpdater: MSIX installation failed (exit code {process.ExitCode}). Error: {error}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "GitHubAutoUpdater: Error installing MSIX");
            return false;
        }
    }

    //delete old update files
    private static void CleanupDownloads()
    {
        try
        {
            if (Directory.Exists(UpdateDirectory))
            {
                Directory.Delete(UpdateDirectory, true);
                Logger.Info("GitHubAutoUpdater: Cleaned up update downloads.");
            }
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "GitHubAutoUpdater: Failed to clean up downloads.");
        }
    }

    //restart app 
    public static void RestartApp()
    {
        try
        {
            string? executablePath = Environment.ProcessPath;
            if (executablePath != null)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = executablePath,
                    UseShellExecute = true
                });
            }

            Application.Current.Dispatcher.Invoke(() =>
            {
                Application.Current.Shutdown();
            });
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "GitHubAutoUpdater: Failed to restart app");
        }
    }

    private static string GetCurrentVersion()
    {
        try
        {
            var version = global::Windows.ApplicationModel.Package.Current.Id.Version;
            return $"v{version.Major}.{version.Minor}.{version.Build}";
        }
        catch
        {
            return "debug";
        }
    }

    private static bool IsNewerVersion(string currentVersion, string newestVersion)
    {
        try
        {
            var current = Version.Parse(currentVersion.TrimStart('v'));
            var newest = Version.Parse(newestVersion.TrimStart('v'));
            return newest > current;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"GitHubAutoUpdater: Failed to compare versions: {currentVersion} vs {newestVersion}");
            return false;
        }
    }

    //github release
    private class GitHubRelease
    {
        public string TagName { get; set; } = string.Empty;
        public string HtmlUrl { get; set; } = string.Empty;
        public List<GitHubAsset> Assets { get; set; } = [];
    }

    //downloadable assets from github releases
    private class GitHubAsset
    {
        public string Name { get; set; } = string.Empty;
        public string BrowserDownloadUrl { get; set; } = string.Empty;
        public long Size { get; set; }
    }
}

#endif
