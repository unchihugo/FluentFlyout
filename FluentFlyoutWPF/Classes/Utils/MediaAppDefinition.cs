// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

namespace FluentFlyout.Classes.Utils;

/// <summary>
/// Describes a media application that FluentFlyout can launch.
/// </summary>
/// <param name="DisplayName">Brand name shown in notifications (not localized).</param>
/// <param name="LaunchUri">URI launched through the Windows shell, e.g. "spotify:".</param>
/// <param name="SessionIdPrefixes">
/// Case-insensitive prefixes of the media session ids that belong to the app (desktop and Store installs differ).
/// A session is only ever controlled when its id starts with one of these, so entries must be specific.
/// </param>
internal sealed record MediaAppDefinition(string DisplayName, string LaunchUri, IReadOnlyList<string> SessionIdPrefixes);

internal enum MediaAppLaunchResult
{
    Started,
    NotInstalled,
    Failed
}