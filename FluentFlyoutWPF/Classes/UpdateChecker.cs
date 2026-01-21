using System.Net.Http;
using System.Text.Json;
using NLog;

namespace FluentFlyout.Classes;

/// <summary>
/// Handles checking for application updates from the API
/// </summary>
public static class UpdateChecker
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private static readonly HttpClient HttpClient = new();
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
        // prevent indefinite hangs
        HttpClient.Timeout = TimeSpan.FromSeconds(10);

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
            result.IsUpdateAvailable = currentVersion != result.NewestVersion;
            
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
}
