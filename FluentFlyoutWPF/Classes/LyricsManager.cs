// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace FluentFlyoutWPF.Classes
{
    public class LrcLine
    {
        public TimeSpan Timestamp { get; set; }
        public string Text { get; set; } = string.Empty;
    }

    public class LyricsManager
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
        private static readonly HttpClient client;

        static LyricsManager()
        {
            client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("FluentFlyoutLyrics/1.0 (https://github.com/unchihugo/FluentFlyout)");
            client.Timeout = TimeSpan.FromSeconds(15);
        }

        public class LrcResponse
        {
            public int id { get; set; }
            public string? trackName { get; set; }
            public string? artistName { get; set; }
            public string? albumName { get; set; }
            public double duration { get; set; }
            public bool instrumental { get; set; }
            public string? plainLyrics { get; set; }
            public string? syncedLyrics { get; set; }
        }

        public static async Task<List<LrcLine>> FetchLyricsAsync(string artist, string title, double durationSeconds)
        {
            if (string.IsNullOrWhiteSpace(artist) || string.IsNullOrWhiteSpace(title))
                return new List<LrcLine>();

            try
            {
                // 1. Try GET /api/get
                string url = $"https://lrclib.net/api/get?artist_name={Uri.EscapeDataString(artist)}&track_name={Uri.EscapeDataString(title)}";
                if (durationSeconds > 0)
                {
                    url += $"&duration={(int)durationSeconds}";
                }

                Logger.Info($"Fetching lyrics from: {url}");
                var response = await client.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var record = JsonSerializer.Deserialize<LrcResponse>(json);
                    if (record != null)
                    {
                        if (record.instrumental)
                        {
                            return new List<LrcLine> { new LrcLine { Timestamp = TimeSpan.Zero, Text = "[Instrumental]" } };
                        }
                        if (!string.IsNullOrEmpty(record.syncedLyrics))
                        {
                            return ParseLrc(record.syncedLyrics);
                        }
                        else if (!string.IsNullOrEmpty(record.plainLyrics))
                        {
                            return ParsePlainLyrics(record.plainLyrics, durationSeconds);
                        }
                    }
                }

                // 2. Try search fallback if GET failed or didn't return synced lyrics
                string searchUrl = $"https://lrclib.net/api/search?q={Uri.EscapeDataString(artist + " " + title)}";
                Logger.Info($"Fallback search lyrics from: {searchUrl}");
                var searchResponse = await client.GetAsync(searchUrl);
                if (searchResponse.IsSuccessStatusCode)
                {
                    var searchJson = await searchResponse.Content.ReadAsStringAsync();
                    var list = JsonSerializer.Deserialize<List<LrcResponse>>(searchJson);
                    if (list != null && list.Count > 0)
                    {
                        var match = list.FirstOrDefault(x => !string.IsNullOrEmpty(x.syncedLyrics))
                                    ?? list.FirstOrDefault(x => !string.IsNullOrEmpty(x.plainLyrics))
                                    ?? list.FirstOrDefault(x => x.instrumental);

                        if (match != null)
                        {
                            if (match.instrumental)
                            {
                                return new List<LrcLine> { new LrcLine { Timestamp = TimeSpan.Zero, Text = "[Instrumental]" } };
                            }
                            if (!string.IsNullOrEmpty(match.syncedLyrics))
                            {
                                return ParseLrc(match.syncedLyrics);
                            }
                            else if (!string.IsNullOrEmpty(match.plainLyrics))
                            {
                                return ParsePlainLyrics(match.plainLyrics, durationSeconds);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error fetching lyrics from LRCLIB");
            }

            return new List<LrcLine>();
        }

        private static List<LrcLine> ParseLrc(string lrcText)
        {
            var lines = new List<LrcLine>();
            if (string.IsNullOrEmpty(lrcText)) return lines;

            foreach (var line in lrcText.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = line.Trim();
                if (!trimmed.StartsWith("[") || !trimmed.Contains("]")) continue;

                int closeIndex = trimmed.IndexOf(']');
                string timePart = trimmed.Substring(1, closeIndex - 1);
                string textPart = trimmed.Substring(closeIndex + 1).Trim();

                string[] colonParts = timePart.Split(':');
                if (colonParts.Length >= 2)
                {
                    if (int.TryParse(colonParts[0], out int minutes))
                    {
                        string[] dotParts = colonParts[1].Split('.');
                        if (int.TryParse(dotParts[0], out int seconds))
                        {
                            int milliseconds = 0;
                            if (dotParts.Length >= 2 && int.TryParse(dotParts[1], out int fraction))
                            {
                                if (dotParts[1].Length == 2)
                                    milliseconds = fraction * 10;
                                else if (dotParts[1].Length == 1)
                                    milliseconds = fraction * 100;
                                else
                                    milliseconds = fraction;
                            }
                            var ts = new TimeSpan(0, 0, minutes, seconds, milliseconds);
                            lines.Add(new LrcLine { Timestamp = ts, Text = textPart });
                        }
                    }
                }
            }
            return lines.OrderBy(l => l.Timestamp).ToList();
        }

        private static List<LrcLine> ParsePlainLyrics(string plainLyrics, double durationSeconds)
        {
            var lines = plainLyrics.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                                   .Select(x => x.Trim())
                                   .Where(x => !string.IsNullOrEmpty(x))
                                   .ToList();

            var result = new List<LrcLine>();
            if (lines.Count == 0) return result;

            double step = (durationSeconds > 0 ? durationSeconds : 180) / lines.Count;
            for (int i = 0; i < lines.Count; i++)
            {
                result.Add(new LrcLine
                {
                    Timestamp = TimeSpan.FromSeconds(i * step),
                    Text = lines[i]
                });
            }
            return result;
        }
    }
}