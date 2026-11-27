# Temporary manual player selection

This change releases a manual player selection when another allowed player
starts playing. It clears the existing pinned ID and lets Automatic selection
choose the target; it does not pin the player that caused the transition.

The option remains on the regular Media flyout settings page and defaults to on.
When off, manual selection keeps its existing behavior. Selection persistence,
the player menu, and the SplitButton are unchanged.

## Playback transitions

Startup records the existing sessions without releasing a saved selection.
Each manual choice samples the current playback states again, so a player that
was already playing cannot cancel that choice with another Playing notification.

Only a transition from non-playing or previously unseen to Playing can release
the selection. Observations continue while the option is off, in Automatic mode,
and for filtered players. Enabling the option or changing a filter therefore
does not turn an old Playing state into a new start. A newly opened Playing
session is observed immediately; closing it removes its observation.

Callbacks read the current state under the same lock used for manual selection,
rather than trusting a delayed event snapshot. Missing information or an
unavailable COM endpoint leaves any previous observation intact. This does not
rebind stale native sessions or fix the separate media-library defect (4P).

## Run the regression checks

From the repository root with PowerShell and the `.NET 10 SDK`:

```powershell
pwsh -NoProfile -File tests/MediaSessionAutoFollow/Run.ps1
```

The runner compiles the production state-observation and selection methods. It
extracts the playback callback's auto-follow and pause-check calls in source
order, plus the real session-open/close subscriptions and unsubscriptions.
SMTC, settings storage, pause actions, and UI refresh are replaced by test
substitutes. Generated source stays in the ignored `obj` directory. No real
player, user configuration, or application process is changed.

The checks cover duplicate Playing notifications, actual resumes, enabling the
option, reselection, startup, opening and closing sessions, filters, missing
information, delayed snapshots, unavailable endpoints, and concurrent duplicates.
To compare an earlier implementation that already has auto-follow, use
`Run.ps1 -Revision <commit>` without changing the checkout.

## Scope of the independent change

This branch is based on master and does not include the background-event display
fix (#1230) or the taskbar/flyout synchronization fix (#1231). The state-transition
checks run independently of both. Complete UI synchronization still benefits
from those separate fixes. This change adds its own open/close listeners only
for playback observation; it adds no focus handler or display-refresh logic.
