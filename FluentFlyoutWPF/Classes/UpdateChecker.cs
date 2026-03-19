// Copyright © 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using NLog;
using System.Net.Http;
using System.Text.Json;

namespace FluentFlyout.Classes;

/// <summary>
/// Handles checking for application updates from the API
/// </summary>
public static class UpdateChecker
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(10) };
    private const string ApiEndpoint = "https://fluentflyout.com/api/newest-version";

    /// <summary>
    /// Result of an update check
    /// </summary>
    public class UpdateCheckResult
    {
        public bool IsUpdateAvailable { get; set; }
        public string NewestVersion { get; set; } = string.Empty;
        public string UpdateUrl { get; set; } = string.Empty;
        public DateTime CheckedAt { get; set; }
        public bool Success { get; set; }
    }

    /// <summary>
    /// Check for updates from the API
    /// </summary>
    /// <param name="currentVersion">The current app version (e.g., "v2.5.0")</param>
    /// <returns>UpdateCheckResult with update information</returns>
    public static async Task<UpdateCheckResult> CheckForUpdatesAsync(string currentVersion)
    {
        var result = new UpdateCheckResult
        {
            CheckedAt = DateTime.Now
        };

        try
        {
            var response = await HttpClient.GetStringAsync(ApiEndpoint);
            var json = JsonDocument.Parse(response);
            
            result.NewestVersion = json.RootElement.GetProperty("version").GetString() ?? string.Empty;
            result.UpdateUrl = json.RootElement.GetProperty("url").GetString() ?? string.Empty;
            result.Success = true;

            // Compare versions
            result.IsUpdateAvailable = currentVersion != "debug" && IsNewerVersion(currentVersion, result.NewestVersion);

            Logger.Info($"Update check complete. Current: {currentVersion}, Newest: {result.NewestVersion}, Update available: {result.IsUpdateAvailable}");
        }
        catch (HttpRequestException ex)
        {
            Logger.Info($"Failed to check for updates - network error: {ex.Message}");
            result.Success = false;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Unexpected error checking for updates");
            result.Success = false;
        }

        return result;
    }

    public static void OpenUpdateUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return;
        
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to open update URL");
        }
    }

#if GITHUB_RELEASE
    /// <summary>
    /// Information about a GitHub Release asset
    /// </summary>
    public class GitHubReleaseAsset
    {
        public string DownloadUrl { get; set; } = string.Empty;
        public long Size { get; set; }
        public string Name { get; set; } = string.Empty;
        public string TagName { get; set; } = string.Empty;
    }

    private const string GitHubApiEndpoint = "https://api.github.com/repos/unchihugo/FluentFlyout/releases/latest";

    /// <summary>
    /// Fetches the latest .msixbundle asset from GitHub Releases
    /// </summary>
    public static async Task<GitHubReleaseAsset?> GetGitHubReleaseAssetAsync()
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, GitHubApiEndpoint);
            request.Headers.Add("User-Agent", "FluentFlyout-AutoUpdater");
            request.Headers.Add("Accept", "application/vnd.github.v3+json");

            using var response = await HttpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var tagName = json.RootElement.GetProperty("tag_name").GetString() ?? string.Empty;
            var assets = json.RootElement.GetProperty("assets");

            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? string.Empty;
                if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    var downloadUrl = asset.GetProperty("browser_download_url").GetString() ?? string.Empty;

                    // Security: enforce HTTPS
                    if (!downloadUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        Logger.Warn("Rejected non-HTTPS download URL: {Url}", downloadUrl);
                        return null;
                    }

                    return new GitHubReleaseAsset
                    {
                        DownloadUrl = downloadUrl,
                        Size = asset.GetProperty("size").GetInt64(),
                        Name = name,
                        TagName = tagName
                    };
                }
            }

            Logger.Warn("No .zip installer asset found in latest GitHub release");
            return null;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to fetch GitHub release asset");
            return null;
        }
    }
#endif

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
            Logger.Error(ex, $"Failed to compare versions: {currentVersion} vs {newestVersion}");
            return false;
        }
    }
}
