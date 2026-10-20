# Media session event isolation

Run from the repository root with PowerShell 7:

```powershell
pwsh -NoProfile -File tests/MediaSessionEventIsolation/Test.ps1
```

The check compiles and executes the production playback and media-property callbacks
from `MainWindow.xaml.cs`, with simulated SMTC sessions and UI dependencies. It
checks event routing, current playback state, metadata deduplication across players,
and preservation of pause-other-sessions calls. It does not start FluentFlyout or
test Windows event delivery, actual playback controls, rendering, or duplicate
AppIDs. `-SourcePath` can select an unfixed source file for a red/green comparison.

The checks were run on master `257be39` before the fix: seven failed and the
no-active-player check passed. With the fix all eight pass.

## Manual acceptance

Use two different SMTC applications, such as Spotify and Edge. Temporarily disable
"Pause other sessions" so both sessions can change independently.

1. Select Spotify in the player menu and keep the flyout open. Start, pause, and
   resume Edge. Spotify's play/pause state and advancing seekbar must remain
   Spotify's, and the taskbar must continue showing Spotify.
2. Change Edge's track or video while Spotify remains selected. It must not trigger
   a Spotify metadata refresh or Next Up popup. Changing Spotify's track must still
   refresh both displays, and selected-player Next Up notifications must still work.
3. With "Pause other sessions" enabled, start Edge while Spotify is selected and
   playing. Spotify must still be paused by the existing auto-pause feature; the new
   UI event filter must not suppress this action.
4. Where two players report the same title, artist, artwork, and playback state,
   select the other player. Its first metadata event must not be discarded as a
   duplicate of the previous player's event.

These are correctness changes only: manual selection persistence, automatic mode,
menus, settings, and control-target selection policy are unchanged.
