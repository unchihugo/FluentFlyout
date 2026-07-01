using DiscordRPC;
using DiscordRPC.Logging;
using FluentFlyout.Classes.Settings;
using System;

namespace FluentFlyoutWPF.Classes.Services
{
    public static class DiscordRpcService
    {
        private static DiscordRpcClient? _client;
        private static bool _isInitialized;
        private static readonly object _lock = new object();
        private const string AppId = "1521650050600796262";

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
                    _client.ClearPresence();
                    return;
                }

                var presence = new RichPresence()
                {
                    Details = Truncate(title, 128),
                    State = Truncate(string.IsNullOrWhiteSpace(artist) ? "Unknown Artist" : artist, 128),
                    Assets = new Assets()
                    {
                        LargeImageKey = "fluentflyout_logo",
                        LargeImageText = "FluentFlyout"
                    }
                };

                if (isPlaying)
                {
                    if (position.HasValue)
                    {
                        var startDateTime = DateTime.UtcNow - position.Value;
                        presence.Timestamps = new Timestamps(startDateTime);
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
                if (_isInitialized && _client != null)
                {
                    _client.ClearPresence();
                }
            }
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