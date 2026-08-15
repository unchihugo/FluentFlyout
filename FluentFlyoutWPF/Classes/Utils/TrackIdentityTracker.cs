// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

namespace FluentFlyout.Classes.Utils;

internal sealed class TrackIdentityTracker
{
    private string _identity = string.Empty;
    private string _lastTitle = string.Empty;
    private string _lastArtist = string.Empty;

    public bool IsLyricsUpdate { get; private set; }
    public string DisplayArtist { get; private set; } = string.Empty;

    public bool Update(string title, string artist)
    {
        // Some media players put synced lyrics in Title and expose the original
        // metadata in Artist as "title - artist". In that case Artist is the
        // identity saved from the original metadata, so this is still the same track.
        bool isKnownLyricsMetadata = !string.IsNullOrEmpty(_identity)
            && string.Equals(artist, _identity, StringComparison.Ordinal);

        // If FluentFlyout starts while lyrics are already active, the original metadata
        // was never observed. A stable compound Artist value across changing Title values
        // is the same lyrics pattern, so learn Artist as the track identity here.
        bool isContinuingLyricsMetadata = !string.IsNullOrEmpty(_lastArtist)
            && string.Equals(artist, _lastArtist, StringComparison.Ordinal)
            && !string.Equals(title, _lastTitle, StringComparison.Ordinal)
            && artist.Contains(" - ", StringComparison.Ordinal);

        // Starting FluentFlyout while lyrics are already active means the original
        // title/artist event was missed. The bridge's compound Artist still gives us
        // a stable identity and the artist to display.
        bool isInitialLyricsMetadata = string.IsNullOrEmpty(_identity)
            && TrySplitLyricsArtist(artist, out string initialTitle, out _)
            && !string.Equals(title, initialTitle, StringComparison.Ordinal);

        if (isKnownLyricsMetadata || isContinuingLyricsMetadata || isInitialLyricsMetadata)
        {
            IsLyricsUpdate = true;
            _identity = artist;
            if (string.IsNullOrEmpty(DisplayArtist)
                && TrySplitLyricsArtist(artist, out _, out string originalArtist))
            {
                DisplayArtist = originalArtist;
            }
            _lastTitle = title;
            _lastArtist = artist;
            return false;
        }

        IsLyricsUpdate = false;
        string newIdentity = $"{title} - {artist}";
        if (string.Equals(newIdentity, _identity, StringComparison.Ordinal))
        {
            _lastTitle = title;
            _lastArtist = artist;
            return false;
        }

        _identity = newIdentity;
        DisplayArtist = artist;
        _lastTitle = title;
        _lastArtist = artist;
        return true;
    }

    private static bool TrySplitLyricsArtist(string artist, out string title, out string originalArtist)
    {
        int separator = artist.LastIndexOf(" - ", StringComparison.Ordinal);
        if (separator <= 0 || separator + 3 >= artist.Length)
        {
            title = string.Empty;
            originalArtist = string.Empty;
            return false;
        }

        title = artist[..separator];
        originalArtist = artist[(separator + 3)..];
        return true;
    }

    public void Reset()
    {
        _identity = string.Empty;
        _lastTitle = string.Empty;
        _lastArtist = string.Empty;
        IsLyricsUpdate = false;
        DisplayArtist = string.Empty;
    }
}
