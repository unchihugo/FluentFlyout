// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;

namespace FluentFlyoutWPF.Classes.Utils;

public readonly record struct CodexProviderConnection(
    string Id,
    int Priority,
    bool IsActive);

public readonly record struct CodexUsageQuota(
    int Used,
    int Total,
    int Remaining,
    DateTime? ResetAtUtc,
    bool Unlimited);

public readonly record struct CodexUsageQuotaBarRow(
    string Label,
    string UsageText,
    string RemainingPercentText,
    double FillRatio,
    string ResetText);

public readonly record struct CodexUsageSnapshot(
    string ConnectionId,
    string Plan,
    bool LimitReached,
    bool ReviewLimitReached,
    CodexUsageQuota? Session,
    CodexUsageQuota? Weekly)
{
    public bool HasData => Session.HasValue || Weekly.HasValue;
}

public static class CodexUsageTextFormatter
{
    public static CodexUsageQuotaBarRow[] FormatQuotaBars(CodexUsageSnapshot snapshot, DateTime nowUtc)
    {
        List<CodexUsageQuotaBarRow> rows = [];

        if (snapshot.Session is { } session)
            rows.Add(FormatQuotaBar("session", session, nowUtc));

        if (snapshot.Weekly is { } weekly)
            rows.Add(FormatQuotaBar("weekly", weekly, nowUtc));

        return [.. rows];
    }

    public static string FormatInline(CodexUsageSnapshot snapshot, DateTime nowUtc)
    {
        List<string> parts = [];

        if (snapshot.Session is { } session)
            parts.Add(FormatQuota("session", session, nowUtc));

        if (snapshot.Weekly is { } weekly)
            parts.Add(FormatQuota("weekly", weekly, nowUtc));

        return string.Join(" | ", parts);
    }

    private static CodexUsageQuotaBarRow FormatQuotaBar(string label, CodexUsageQuota quota, DateTime nowUtc)
    {
        string totalText = FormatTotal(quota);
        int remainingPercent = GetRemainingPercent(quota);

        double fillRatio = !quota.Unlimited && quota.Total > 0
            ? Math.Clamp(quota.Remaining / (double)quota.Total, 0d, 1d)
            : 0d;

        return new CodexUsageQuotaBarRow(
            label,
            string.Create(CultureInfo.InvariantCulture, $"{quota.Used}/{totalText}"),
            quota.Unlimited || quota.Total <= 0
                ? "--"
                : string.Create(CultureInfo.InvariantCulture, $"{remainingPercent}%"),
            fillRatio,
            FormatResetCountdown(quota.ResetAtUtc, nowUtc));
    }

    private static string FormatQuota(string label, CodexUsageQuota quota, DateTime nowUtc)
    {
        return quota.Unlimited || quota.Total <= 0
            ? string.Create(CultureInfo.InvariantCulture, $"{label} {quota.Used} / {FormatTotal(quota)} · -- · {FormatResetCountdown(quota.ResetAtUtc, nowUtc)}")
            : string.Create(CultureInfo.InvariantCulture, $"{label} {quota.Used} / {FormatTotal(quota)} · {GetRemainingPercent(quota)}% · {FormatResetCountdown(quota.ResetAtUtc, nowUtc)}");
    }

    private static string FormatTotal(CodexUsageQuota quota)
    {
        return quota.Unlimited || quota.Total <= 0
            ? "--"
            : quota.Total.ToString(CultureInfo.InvariantCulture);
    }

    private static int GetRemainingPercent(CodexUsageQuota quota)
    {
        return quota.Total > 0
            ? (int)Math.Round(quota.Remaining * 100d / quota.Total, MidpointRounding.AwayFromZero)
            : 0;
    }

    public static string FormatResetCountdown(DateTime? resetAtUtc, DateTime nowUtc)
    {
        if (!resetAtUtc.HasValue)
            return "in --";

        TimeSpan remaining = resetAtUtc.Value - nowUtc;
        if (remaining <= TimeSpan.Zero)
            return "in 0s";

        if (remaining.TotalDays >= 1)
        {
            int days = (int)remaining.TotalDays;
            int hours = remaining.Hours;
            int minutes = remaining.Minutes;
            return $"in {days}d {hours}h {minutes}m";
        }

        if (remaining.TotalHours >= 1)
        {
            int hours = (int)remaining.TotalHours;
            int minutes = remaining.Minutes;
            return $"in {hours}h {minutes}m";
        }

        int totalMinutes = (int)remaining.TotalMinutes;
        if (totalMinutes >= 1)
            return $"in {totalMinutes}m {remaining.Seconds}s";

        return $"in {remaining.Seconds}s";
    }
}

public static class TaskbarWidgetPassiveSlotVisibilityHelper
{
    public static bool ShouldShowCodexUsage(
        bool widgetEnabled,
        bool systemStatsEnabled,
        bool codexUsageEnabled,
        bool codexUsageHasData)
    {
        return widgetEnabled
            && systemStatsEnabled
            && codexUsageEnabled
            && codexUsageHasData;
    }

    public static bool ShouldShowCodexUsageInline(
        bool widgetEnabled,
        bool systemStatsEnabled,
        bool codexUsageEnabled,
        bool codexUsageHasData,
        bool pinCodexUsageToClockSide)
    {
        return !pinCodexUsageToClockSide
            && ShouldShowCodexUsage(
                widgetEnabled,
                systemStatsEnabled,
                codexUsageEnabled,
                codexUsageHasData);
    }

    public static bool ShouldShowCodexUsageStandalone(
        bool widgetEnabled,
        bool codexUsageEnabled,
        bool codexUsageHasData,
        bool pinCodexUsageToClockSide)
    {
        return widgetEnabled
            && codexUsageEnabled
            && codexUsageHasData
            && pinCodexUsageToClockSide;
    }
}

public static class CodexUsageApiPayloadParser
{
    public static CodexProviderConnection[] ParseProviderConnections(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        using JsonDocument document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("connections", out JsonElement connectionsElement)
            || connectionsElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<CodexProviderConnection> connections = [];

        foreach (JsonElement connectionElement in connectionsElement.EnumerateArray())
        {
            if (!TryGetString(connectionElement, "provider", out string? provider)
                || !string.Equals(provider, "codex", StringComparison.Ordinal))
            {
                continue;
            }

            if (!TryGetString(connectionElement, "id", out string? id)
                || string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            int priority = TryGetInt32(connectionElement, "priority") ?? int.MaxValue;
            bool isActive = TryGetBoolean(connectionElement, "isActive") ?? false;
            connections.Add(new CodexProviderConnection(id, priority, isActive));
        }

        return [.. connections];
    }

    public static CodexUsageSnapshot[] ParseProviderMetadataSnapshots(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        using JsonDocument document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("connections", out JsonElement connectionsElement)
            || connectionsElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<CodexUsageSnapshot> snapshots = [];
        foreach (JsonElement connectionElement in connectionsElement.EnumerateArray())
        {
            if (TryGetString(connectionElement, "id", out string? id)
                && !string.IsNullOrWhiteSpace(id)
                && TryParseProviderMetadataSnapshot(connectionElement, id, out CodexUsageSnapshot snapshot))
            {
                snapshots.Add(snapshot);
            }
        }

        return [.. snapshots];
    }

    public static CodexUsageSnapshot ParseUsagePayload(string? json, string connectionId)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new CodexUsageSnapshot(connectionId, string.Empty, false, false, null, null);

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        string plan = TryGetString(root, "plan", out string? parsedPlan)
            ? parsedPlan ?? string.Empty
            : string.Empty;
        bool limitReached = TryGetBoolean(root, "limitReached") ?? false;
        bool reviewLimitReached = TryGetBoolean(root, "reviewLimitReached") ?? false;

        CodexUsageQuota? session = null;
        CodexUsageQuota? weekly = null;

        if (root.TryGetProperty("quotas", out JsonElement quotasElement)
            && quotasElement.ValueKind == JsonValueKind.Object)
        {
            if (TryParseQuota(quotasElement, "session", out CodexUsageQuota parsedSession))
                session = parsedSession;

            if (TryParseQuota(quotasElement, "weekly", out CodexUsageQuota parsedWeekly))
                weekly = parsedWeekly;
        }

        if (!session.HasValue
            && !weekly.HasValue
            && TryParseProviderMetadataSnapshot(root, connectionId, out CodexUsageSnapshot metadataSnapshot))
        {
            return metadataSnapshot;
        }

        return new CodexUsageSnapshot(
            connectionId,
            plan,
            limitReached,
            reviewLimitReached,
            session,
            weekly);
    }

    private static bool TryParseProviderMetadataSnapshot(JsonElement root, string connectionId, out CodexUsageSnapshot snapshot)
    {
        snapshot = default;

        if (TryGetString(root, "provider", out string? provider)
            && !string.Equals(provider, "codex", StringComparison.Ordinal))
        {
            return false;
        }

        string plan = TryGetProviderPlan(root);
        bool limitReached = TryGetBoolean(root, "limitReached") ?? IsProviderUsageLimited(root);
        DateTime? resetAtUtc = TryGetLatestModelLockUtc(root);
        if (resetAtUtc.HasValue)
            limitReached = true;

        bool hasCodexMetadata = !string.IsNullOrWhiteSpace(plan)
            || resetAtUtc.HasValue
            || TryGetString(root, "testStatus", out _);
        if (!hasCodexMetadata)
            return false;

        CodexUsageQuota session = limitReached || resetAtUtc.HasValue
            ? new CodexUsageQuota(1, 1, 0, resetAtUtc, false)
            : new CodexUsageQuota(0, 0, 0, null, true);

        snapshot = new CodexUsageSnapshot(
            connectionId,
            plan,
            limitReached,
            false,
            session,
            null);
        return true;
    }

    private static string TryGetProviderPlan(JsonElement root)
    {
        if (TryGetString(root, "plan", out string? plan)
            && !string.IsNullOrWhiteSpace(plan))
        {
            return plan;
        }

        if (root.TryGetProperty("providerSpecificData", out JsonElement providerData)
            && providerData.ValueKind == JsonValueKind.Object
            && TryGetString(providerData, "chatgptPlanType", out string? providerPlan)
            && !string.IsNullOrWhiteSpace(providerPlan))
        {
            return providerPlan;
        }

        return string.Empty;
    }

    private static bool IsProviderUsageLimited(JsonElement root)
    {
        if (TryGetString(root, "lastError", out string? lastError)
            && !string.IsNullOrWhiteSpace(lastError))
        {
            return lastError.Contains("usage limit", StringComparison.OrdinalIgnoreCase)
                || lastError.Contains("rate limit", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static DateTime? TryGetLatestModelLockUtc(JsonElement root)
    {
        DateTime? latest = null;

        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (!property.Name.StartsWith("modelLock_", StringComparison.Ordinal)
                || property.Value.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            string? rawValue = property.Value.GetString();
            if (!DateTime.TryParse(
                    rawValue,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                    out DateTime parsedValue))
            {
                continue;
            }

            if (!latest.HasValue || parsedValue > latest.Value)
                latest = parsedValue;
        }

        return latest;
    }

    private static bool TryParseQuota(JsonElement quotasElement, string quotaName, out CodexUsageQuota quota)
    {
        quota = default;

        if (!quotasElement.TryGetProperty(quotaName, out JsonElement quotaElement)
            || quotaElement.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        int used = TryGetInt32(quotaElement, "used") ?? 0;
        int total = TryGetInt32(quotaElement, "total") ?? 0;
        int remaining = TryGetInt32(quotaElement, "remaining") ?? 0;
        bool unlimited = TryGetBoolean(quotaElement, "unlimited") ?? false;
        DateTime? resetAtUtc = TryGetDateTime(quotaElement, "resetAt");

        quota = new CodexUsageQuota(
            used,
            total,
            remaining,
            resetAtUtc,
            unlimited);
        return true;
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string? value)
    {
        value = null;

        if (!element.TryGetProperty(propertyName, out JsonElement property)
            || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString();
        return true;
    }

    private static int? TryGetInt32(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property))
            return null;

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out int intValue))
            return intValue;

        if (property.ValueKind == JsonValueKind.String
            && int.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedValue))
        {
            return parsedValue;
        }

        return null;
    }

    private static bool? TryGetBoolean(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property))
            return null;

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(property.GetString(), out bool value) => value,
            _ => null
        };
    }

    private static DateTime? TryGetDateTime(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property)
            || property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        string? rawValue = property.GetString();
        if (!DateTime.TryParse(
                rawValue,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out DateTime parsedValue))
        {
            return null;
        }

        return parsedValue;
    }
}

public static class CodexUsageSnapshotSelector
{
    public static CodexUsageSnapshot SelectBestSnapshot(
        IEnumerable<CodexProviderConnection> connections,
        IReadOnlyDictionary<string, CodexUsageSnapshot> snapshots)
    {
        CodexUsageSnapshot bestSnapshot = default;
        CodexProviderConnection bestConnection = default;
        bool hasBest = false;

        foreach (CodexProviderConnection connection in connections)
        {
            if (!connection.IsActive
                || !snapshots.TryGetValue(connection.Id, out CodexUsageSnapshot snapshot)
                || !snapshot.HasData)
            {
                continue;
            }

            if (!hasBest || IsBetterCandidate(snapshot, connection, bestSnapshot, bestConnection))
            {
                bestSnapshot = snapshot;
                bestConnection = connection;
                hasBest = true;
            }
        }

        return bestSnapshot;
    }

    private static bool IsBetterCandidate(
        CodexUsageSnapshot candidateSnapshot,
        CodexProviderConnection candidateConnection,
        CodexUsageSnapshot currentSnapshot,
        CodexProviderConnection currentConnection)
    {
        int candidateQuotaCount = GetQuotaCount(candidateSnapshot);
        int currentQuotaCount = GetQuotaCount(currentSnapshot);
        if (candidateQuotaCount != currentQuotaCount)
            return candidateQuotaCount > currentQuotaCount;

        bool candidateHasWeekly = candidateSnapshot.Weekly.HasValue;
        bool currentHasWeekly = currentSnapshot.Weekly.HasValue;
        if (candidateHasWeekly != currentHasWeekly)
            return candidateHasWeekly;

        bool candidateHealthy = !candidateSnapshot.LimitReached;
        bool currentHealthy = !currentSnapshot.LimitReached;
        if (candidateHealthy != currentHealthy)
            return candidateHealthy;

        int candidatePlanScore = GetPlanScore(candidateSnapshot.Plan);
        int currentPlanScore = GetPlanScore(currentSnapshot.Plan);
        if (candidatePlanScore != currentPlanScore)
            return candidatePlanScore > currentPlanScore;

        if (candidateConnection.Priority != currentConnection.Priority)
            return candidateConnection.Priority < currentConnection.Priority;

        return string.CompareOrdinal(candidateSnapshot.ConnectionId, currentSnapshot.ConnectionId) < 0;
    }

    private static int GetQuotaCount(CodexUsageSnapshot snapshot)
    {
        int count = 0;

        if (snapshot.Session.HasValue)
            count++;

        if (snapshot.Weekly.HasValue)
            count++;

        return count;
    }

    private static int GetPlanScore(string? plan)
    {
        return plan?.Trim().ToLowerInvariant() switch
        {
            "enterprise" => 6,
            "business" => 5,
            "pro" => 4,
            "plus" => 4,
            "team" => 3,
            "go" => 2,
            "free" => 1,
            _ => 0
        };
    }
}

public static class CodexUsageCliTokenProvider
{
    public const string HeaderName = "x-9r-cli-token";
    private const string TokenSalt = "9r-cli-auth";
    private const int TokenLength = 16;
    private const string AuthDirectoryName = "auth";
    private const string CliSecretFileName = "cli-secret";

    public static bool TryApplyHeader(HttpRequestMessage request, string? dataDirectory = null)
    {
        if (!IsLocalRequest(request.RequestUri))
            return false;

        string resolvedDataDirectory = dataDirectory ?? GetDefaultDataDirectory();
        if (!TryReadMachineId(resolvedDataDirectory, out string? machineId)
            || !TryReadCliSecret(resolvedDataDirectory, out string? cliSecret)
            || !TryCreateToken(machineId, cliSecret, out string? token))
        {
            return false;
        }

        request.Headers.Remove(HeaderName);
        return request.Headers.TryAddWithoutValidation(HeaderName, token);
    }

    public static bool TryCreateToken(string? machineId, out string? token)
    {
        return TryCreateToken(machineId, null, out token);
    }

    public static bool TryCreateToken(string? machineId, string? cliSecret, out string? token)
    {
        token = null;
        string trimmedMachineId = machineId?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(trimmedMachineId))
            return false;

        string trimmedCliSecret = cliSecret?.Trim() ?? string.Empty;
        string tokenSource = string.IsNullOrEmpty(trimmedCliSecret)
            ? trimmedMachineId + TokenSalt
            : trimmedMachineId + TokenSalt + trimmedCliSecret;
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(tokenSource));
        token = Convert.ToHexString(hash).ToLowerInvariant()[..TokenLength];
        return true;
    }

    private static bool TryReadMachineId(string dataDirectory, out string? machineId)
    {
        machineId = null;

        try
        {
            string path = Path.Combine(dataDirectory, "machine-id");
            if (!File.Exists(path))
                return false;

            machineId = File.ReadAllText(path).Trim();
            return !string.IsNullOrEmpty(machineId);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryReadCliSecret(string dataDirectory, out string? cliSecret)
    {
        cliSecret = null;

        try
        {
            string path = Path.Combine(dataDirectory, AuthDirectoryName, CliSecretFileName);
            if (!File.Exists(path))
                return true;

            cliSecret = File.ReadAllText(path).Trim();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string GetDefaultDataDirectory()
    {
        string? appData = Environment.GetEnvironmentVariable("APPDATA");
        if (string.IsNullOrWhiteSpace(appData))
            appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        return Path.Combine(appData, "9router");
    }

    private static bool IsLocalRequest(Uri? uri)
    {
        if (uri == null || !uri.IsAbsoluteUri)
            return false;

        return uri.IsLoopback
            || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class CodexUsageWidgetService : IDisposable
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly bool _ownsHttpClient;
    private bool _isRefreshing;
    private bool _disposed;
    private CodexUsageSnapshot _currentSnapshot;

    public CodexUsageWidgetService(HttpClient? httpClient = null, string? baseUrl = null)
    {
        _baseUrl = (baseUrl ?? "http://localhost:20128").TrimEnd('/');
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        _ownsHttpClient = httpClient is null;

        _refreshTimer.Tick += RefreshTimer_Tick;
        Refresh();
        _refreshTimer.Start();
    }

    public event EventHandler? SnapshotChanged;

    public CodexUsageSnapshot CurrentSnapshot => _currentSnapshot;

    public bool HasData => _currentSnapshot.HasData;

    public void Refresh()
    {
        _ = RefreshAsync();
    }

    private async void RefreshTimer_Tick(object? sender, EventArgs e)
    {
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (_disposed || _isRefreshing)
            return;

        _isRefreshing = true;

        try
        {
            CodexUsageSnapshot nextSnapshot = await LoadSnapshotAsync();
            if (!nextSnapshot.Equals(_currentSnapshot))
            {
                _currentSnapshot = nextSnapshot;
                SnapshotChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Failed to refresh Codex usage widget from 9router");

            if (_currentSnapshot.HasData)
            {
                _currentSnapshot = default;
                SnapshotChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private async Task<CodexUsageSnapshot> LoadSnapshotAsync()
    {
        string? providersJson = await TryGetStringAsync("/api/providers");
        if (string.IsNullOrWhiteSpace(providersJson))
            return default;

        CodexProviderConnection[] connections = CodexUsageApiPayloadParser.ParseProviderConnections(providersJson);
        if (connections.Length == 0)
            return default;

        List<CodexProviderConnection> activeConnections = [.. connections.Where(connection => connection.IsActive)];
        if (activeConnections.Count == 0)
            return default;

        Dictionary<string, CodexUsageSnapshot> snapshots = new(StringComparer.Ordinal);
        foreach (CodexUsageSnapshot snapshot in CodexUsageApiPayloadParser.ParseProviderMetadataSnapshots(providersJson))
        {
            if (snapshot.HasData)
                snapshots[snapshot.ConnectionId] = snapshot;
        }

        foreach (CodexProviderConnection connection in activeConnections)
        {
            string? usageJson = await TryGetStringAsync($"/api/usage/{Uri.EscapeDataString(connection.Id)}");
            if (string.IsNullOrWhiteSpace(usageJson))
                continue;

            try
            {
                CodexUsageSnapshot snapshot = CodexUsageApiPayloadParser.ParseUsagePayload(usageJson, connection.Id);
                if (snapshot.HasData)
                    snapshots[connection.Id] = snapshot;
            }
            catch (JsonException ex)
            {
                Logger.Debug(ex, "Failed to parse Codex usage payload for connection '{ConnectionId}'", connection.Id);
            }
        }

        return CodexUsageSnapshotSelector.SelectBestSnapshot(activeConnections, snapshots);
    }

    private async Task<string?> TryGetStringAsync(string relativePath)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, _baseUrl + relativePath);
        CodexUsageCliTokenProvider.TryApplyHeader(request);

        using HttpResponseMessage response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            return null;

        return await response.Content.ReadAsStringAsync();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _refreshTimer.Stop();
        _refreshTimer.Tick -= RefreshTimer_Tick;

        if (_ownsHttpClient)
            _httpClient.Dispose();
    }
}
