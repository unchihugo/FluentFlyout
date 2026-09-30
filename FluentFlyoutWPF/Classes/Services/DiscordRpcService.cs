// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using DiscordRPC;
using DiscordRPC.Logging;
using FluentFlyout.Classes.Settings;
using System.Net.Http;
using System.Text.Json;

namespace FluentFlyoutWPF.Classes.Services
{
    public static class DiscordRpcService
    {
        private static DiscordRpcClient? _client;
        private static bool _isInitialized;
        private static readonly object _lock = new object();
        private const string AppId = "1521650050600796262";

        private static readonly HttpClient Http = CreateHttpClient();
        private static readonly Dictionary<string, string?> _coverCache = new(); // null = looked up, no cover found
        private static readonly HashSet<string> _coverLookupsInFlight = new();
        private static (string Title, string Artist, bool IsPlaying, TimeSpan? Position, TimeSpan? EndTime)? _lastRequest;

        private static HttpClient CreateHttpClient()
        {
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("FluentFlyout/1.0 (https://github.com/unchihugo/FluentFlyout)"); // required by MusicBrainz
            return http;
        }

        public static void Initialize()
        {
            lock (_lock)
            {
                if (_isInitialized || !SettingsManager.Current.DiscordRpcEnabled)
                    return;

                _client = new DiscordRpcClient(AppId)
                {
                    Logger = new ConsoleLogger { Level = LogLevel.Warning }
                };

                _client.Initialize();
                _isInitialized = true;
            }
        }

        public static void UpdatePresence(string title, string artist, bool isPlaying, TimeSpan? position = null, TimeSpan? endTime = null)
        {
            lock (_lock)
            {
                if (!SettingsManager.Current.DiscordRpcEnabled)
                {
                    if (_isInitialized)
                        DisposeInternal();
                    return;
                }

                if (!_isInitialized)
                {
                    Initialize();
                }

                if (_client == null || !_client.IsInitialized) return;

                if (string.IsNullOrWhiteSpace(title))
                {
                    _lastRequest = null;
                    _client.ClearPresence();
                    return;
                }

                _lastRequest = (title, artist, isPlaying, position, endTime);
                var coverUrl = GetCoverUrl(title, artist);

                var presence = new RichPresence()
                {
                    Type = ActivityType.Listening,
                    StatusDisplay = StatusDisplayType.Details,
                    Details = Truncate(title, 128),
                    State = Truncate(string.IsNullOrWhiteSpace(artist) ? "Unknown Artist" : artist, 128),
                    Assets = coverUrl != null
                        ? new Assets()
                        {
                            LargeImageKey = coverUrl,
                            SmallImageKey = "fluentflyout_logo",
                            SmallImageText = "FluentFlyout",
                            SmallImageUrl = "https://fluentflyout.com/"
                        }
                        : new Assets()
                        {
                            LargeImageKey = "fluentflyout_logo",
                            LargeImageUrl = "https://fluentflyout.com/"
                        }
                };

                if (isPlaying)
                {
                    if (position.HasValue)
                    {
                        var startDateTime = DateTime.UtcNow - position.Value;
                        if (endTime.HasValue)
                        {
                            var endDateTime = DateTime.UtcNow + (endTime.Value - position.Value);
                            presence.Timestamps = new Timestamps(startDateTime, endDateTime);
                        }
                        else
                        {
                            presence.Timestamps = new Timestamps(startDateTime);
                        }
                    }
                    else
                    {
                        presence.Timestamps = Timestamps.Now;
                    }
                }

                _client.SetPresence(presence);
            }
        }

        public static void ClearPresence()
        {
            lock (_lock)
            {
                _lastRequest = null;
                if (_isInitialized && _client != null)
                {
                    _client.ClearPresence();
                }
            }
        }

        // caller must hold _lock. Returns a cached cover, or null while the lookup runs (presence is re-sent when it finishes)
        private static string? GetCoverUrl(string title, string artist)
        {
            if (string.IsNullOrWhiteSpace(artist)) return null;

            var key = $"{title}\n{artist}";
            if (_coverCache.TryGetValue(key, out var cached)) return cached;

            if (_coverLookupsInFlight.Add(key))
                _ = Task.Run(() => LookupCoverAsync(title, artist, key));

            return null;
        }

        private static async Task LookupCoverAsync(string title, string artist, string key)
        {
            string? coverUrl = null;
            try
            {
                coverUrl = await FindCoverUrlAsync(title, artist);
            }
            catch (Exception)
            {
                // network/parse failure: fall back to the logo
            }

            lock (_lock)
            {
                if (_coverCache.Count > 200) _coverCache.Clear();
                _coverCache[key] = coverUrl;
                _coverLookupsInFlight.Remove(key);

                if (coverUrl != null && _lastRequest is { } r && $"{r.Title}\n{r.Artist}" == key)
                    UpdatePresence(r.Title, r.Artist, r.IsPlaying, r.Position, r.EndTime);
            }
        }

        private static async Task<string?> FindCoverUrlAsync(string title, string artist)
        {
            static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

            var query = Uri.EscapeDataString($"recording:\"{Escape(title)}\" AND artist:\"{Escape(artist)}\"");
            using var response = await Http.GetAsync($"https://musicbrainz.org/ws/2/recording/?query={query}&fmt=json&limit=1");
            if (!response.IsSuccessStatusCode) return null;

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (!json.RootElement.TryGetProperty("recordings", out var recordings) || recordings.GetArrayLength() == 0) return null;

            var recording = recordings[0];
            if (!recording.TryGetProperty("score", out var score) || score.GetInt32() < 90) return null;
            if (!recording.TryGetProperty("releases", out var releases)) return null;

            // the recording may appear on releases without artwork, so check the first few
            foreach (var release in releases.EnumerateArray().Take(3))
            {
                var url = $"https://coverartarchive.org/release/{release.GetProperty("id").GetString()}/front-500";
                using var head = await Http.SendAsync(new HttpRequestMessage(HttpMethod.Head, url));
                if (head.IsSuccessStatusCode) return url;
            }

            return null;
        }

        public static void Dispose()
        {
            lock (_lock)
            {
                DisposeInternal();
            }
        }

        private static void DisposeInternal()
        {
            if (!_isInitialized) return;

            if (_client != null)
            {
                _client.ClearPresence();
                _client.Dispose();
                _client = null;
            }

            _isInitialized = false;
        }

        private static string Truncate(string value, int maxChars)
        {
            if (string.IsNullOrEmpty(value)) return value;
            return value.Length <= maxChars ? value : value.Substring(0, Math.Max(0, maxChars - 3)) + "...";
        }
    }
}
