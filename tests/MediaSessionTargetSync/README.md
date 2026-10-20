# Media session target synchronization checks

Run from the repository root with PowerShell and the .NET 10 SDK:

```powershell
pwsh -NoProfile -File tests/MediaSessionTargetSync/Run.ps1
```

To reproduce against a previous revision without changing the checkout:

```powershell
pwsh -NoProfile -File tests/MediaSessionTargetSync/Run.ps1 -Revision origin/master
```

The runner compiles the actual selection, refresh and lifecycle callback methods
from that source revision, together with its event subscriptions. It substitutes
a queued dispatcher, a media manager and UI sinks to exercise the callbacks without
requiring a live media player or changing the user's settings. No NuGet test
framework is required. Generated source stays in the ignored `obj` directory.

These checks cover paused position changes, visible and hidden focus changes,
session opening/closing, queued callbacks, cleanup and preserved manual choices.
They do not instantiate WPF controls or Windows SMTC. Validate those separately:

1. With Spotify paused and a video in Edge, select each player and then Automatic.
   Taskbar and flyout should agree, including the paused position.
2. Hide the flyout, switch the selected player, then open it again. Its paused
   position should belong to the newly selected player immediately.
3. In Automatic mode, start, close or switch the Windows current player. The
   taskbar and already visible flyout should follow together.
4. Close a manually selected player. Both surfaces should show the fallback;
   closing the final allowed session should clear the taskbar and hide the flyout.

The separate same-app duplicate/stale session defect in the media library is not
fixed here.
