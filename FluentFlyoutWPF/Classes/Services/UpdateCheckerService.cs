// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using NLog;

namespace FluentFlyoutWPF.Classes.Services;

/// <summary>
/// Handles checking for application updates from GitHub Releases (KoksMen/FluentFlyout)
/// </summary>
public static class UpdateCheckerService
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public const string CurrentAppVersion = "2.15.0-f2.0.0";
    public const string GitHubRepoOwner = "KoksMen";
    public const string GitHubRepoName = "FluentFlyout";
    public static string ReleasesApiUrl => $"https://api.github.com/repos/{GitHubRepoOwner}/{GitHubRepoName}/releases/latest";
    public static string ReleasesWebUrl => $"https://github.com/{GitHubRepoOwner}/{GitHubRepoName}/releases";

    private static readonly Lazy<HttpClient> _httpClientLazy = new(() =>
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.Clear();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("FluentFlyout", CurrentAppVersion));
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.Timeout = TimeSpan.FromSeconds(15);
        return client;
    });

    private static HttpClient HttpClient => _httpClientLazy.Value;

    /// <summary>
    /// Result of an update check
    /// </summary>
    public class UpdateCheckResult
    {
        public bool IsUpdateAvailable { get; set; }
        public string NewestVersion { get; set; } = string.Empty;
        public string UpdateUrl { get; set; } = string.Empty;
        public string ReleaseNotes { get; set; } = string.Empty;
        public DateTime CheckedAt { get; set; }
        public bool Success { get; set; }
    }

    /// <summary>
    /// Check for updates from GitHub Releases API
    /// </summary>
    /// <param name="currentVersion">The current app version (e.g., "2.15.0-f2.0.0")</param>
    /// <returns>UpdateCheckResult with update information</returns>
    public static async Task<UpdateCheckResult> CheckForUpdatesAsync(string currentVersion)
    {
        var effectiveVersion = string.IsNullOrWhiteSpace(currentVersion) || currentVersion == "debug"
            ? CurrentAppVersion
            : currentVersion;

        var result = new UpdateCheckResult
        {
            CheckedAt = DateTime.Now
        };

        try
        {
            using var response = await HttpClient.GetAsync(ReleasesApiUrl);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var release = JsonSerializer.Deserialize<GitHubReleaseDto>(json);

            if (release == null || string.IsNullOrWhiteSpace(release.TagName))
            {
                Logger.Warn("GitHub API returned an empty or invalid release payload.");
                result.Success = false;
                return result;
            }

            result.NewestVersion = release.TagName.Trim();
            result.ReleaseNotes = release.Body ?? string.Empty;

            // Select best asset
            var asset = SelectReleaseAsset(release);
            result.UpdateUrl = !string.IsNullOrWhiteSpace(asset?.BrowserDownloadUrl)
                ? asset.BrowserDownloadUrl
                : (!string.IsNullOrWhiteSpace(release.HtmlUrl) ? release.HtmlUrl : ReleasesWebUrl);

            result.Success = true;
            result.IsUpdateAvailable = IsNewerVersion(effectiveVersion, result.NewestVersion);

            Logger.Info($"Update check complete. Current: {effectiveVersion}, Newest: {result.NewestVersion}, Update available: {result.IsUpdateAvailable}, Url: {result.UpdateUrl}");
        }
        catch (HttpRequestException ex)
        {
            Logger.Info($"Failed to check for updates - network error: {ex.Message}");
            result.Success = false;
        }
        catch (TaskCanceledException ex)
        {
            Logger.Info(ex, "Failed to check for updates - request timed out.");
            result.Success = false;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Unexpected error checking for updates");
            result.Success = false;
        }

        return result;
    }

    private static GitHubReleaseAssetDto? SelectReleaseAsset(GitHubReleaseDto release)
    {
        if (release.Assets == null || release.Assets.Count == 0)
        {
            return null;
        }

        // Prioritize:
        // 1. FluentFlyout.exe
        // 2. Portable_x64*.zip or FluentFlyout*x64*.zip
        // 3. Any .zip or .exe
        var directExe = release.Assets.FirstOrDefault(a =>
            string.Equals(a.Name, "FluentFlyout.exe", StringComparison.OrdinalIgnoreCase));
        if (directExe != null) return directExe;

        var portableZip = release.Assets.FirstOrDefault(a =>
            !string.IsNullOrWhiteSpace(a.Name) &&
            a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
            (a.Name.Contains("x64", StringComparison.OrdinalIgnoreCase) || a.Name.Contains("Portable", StringComparison.OrdinalIgnoreCase)));
        if (portableZip != null) return portableZip;

        var anyZip = release.Assets.FirstOrDefault(a =>
            !string.IsNullOrWhiteSpace(a.Name) && a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
        if (anyZip != null) return anyZip;

        var anyExe = release.Assets.FirstOrDefault(a =>
            !string.IsNullOrWhiteSpace(a.Name) && a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
        if (anyExe != null) return anyExe;

        return release.Assets.FirstOrDefault();
    }

    public static void OpenUpdateUrl(string url)
    {
        var targetUrl = !string.IsNullOrEmpty(url) ? url : ReleasesWebUrl;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = targetUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to open update URL");
        }
    }

    public static bool IsNewerVersion(string currentVersion, string newestVersion)
    {
        try
        {
            if (ForkVersion.TryParse(currentVersion, out var current) &&
                ForkVersion.TryParse(newestVersion, out var newest))
            {
                return newest.CompareTo(current) > 0;
            }

            // Fallback to standard Version.Parse
            var curVer = Version.Parse(currentVersion.Trim().TrimStart('v', 'V'));
            var newVer = Version.Parse(newestVersion.Trim().TrimStart('v', 'V'));
            return newVer > curVer;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"Failed to compare versions: '{currentVersion}' vs '{newestVersion}'");
            return false;
        }
    }

    public readonly struct ForkVersion : IComparable<ForkVersion>
    {
        public Version BaseVersion { get; }
        public Version ForkSubVersion { get; }

        public ForkVersion(Version baseVersion, Version forkSubVersion)
        {
            BaseVersion = baseVersion;
            ForkSubVersion = forkSubVersion;
        }

        public static bool TryParse(string? input, out ForkVersion result)
        {
            result = default;
            if (string.IsNullOrWhiteSpace(input)) return false;

            input = input.Trim().TrimStart('v', 'V');

            string baseStr = input;
            string forkStr = "0.0.0";

            int dashIndex = input.IndexOf('-');
            if (dashIndex >= 0)
            {
                baseStr = input.Substring(0, dashIndex);
                forkStr = input.Substring(dashIndex + 1).TrimStart('f', 'F');
            }

            if (!Version.TryParse(baseStr, out var baseVer))
            {
                return false;
            }

            if (!Version.TryParse(forkStr, out var forkVer))
            {
                forkVer = new Version(0, 0, 0);
            }

            result = new ForkVersion(baseVer, forkVer);
            return true;
        }

        public int CompareTo(ForkVersion other)
        {
            int baseCmp = (BaseVersion ?? new Version(0, 0)).CompareTo(other.BaseVersion ?? new Version(0, 0));
            if (baseCmp != 0) return baseCmp;

            return (ForkSubVersion ?? new Version(0, 0)).CompareTo(other.ForkSubVersion ?? new Version(0, 0));
        }
    }

    internal sealed class GitHubReleaseDto
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = string.Empty;

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }

        [JsonPropertyName("body")]
        public string? Body { get; set; }

        [JsonPropertyName("published_at")]
        public DateTime? PublishedAtUtc { get; set; }

        [JsonPropertyName("assets")]
        public List<GitHubReleaseAssetDto> Assets { get; set; } = new();
    }

    internal sealed class GitHubReleaseAssetDto
    {
        [JsonPropertyName("url")]
        public string ApiUrl { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = string.Empty;

        [JsonPropertyName("size")]
        public long Size { get; set; }
    }
}