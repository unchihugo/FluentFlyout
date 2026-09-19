# PiP Toggle — companion extension for FluentFlyout

A tiny Edge/Chrome extension that does one thing: **toggle picture-in-picture for the video a page is playing.**
Its shortcut is *global*, so it also fires while the browser is in the background — that is what FluentFlyout
presses when the taskbar widget is clicked. Nothing in here talks to another program, and no window is created,
moved or hidden.

## Install (once)

1. Open `edge://extensions/`
2. Turn on **Developer mode**
3. Click **Load unpacked** and pick this folder (`browser-extension/pip-toggle`)
4. Open `edge://extensions/shortcuts` and check the shortcut: **Ctrl+Shift+6**, scope **Global**.
   `commands` may only suggest `Ctrl+Shift+0..9` in global mode. If another program on the machine already owns
   that combination, the browser cannot register it and the shortcut silently does nothing — pick a free one and
   update `BrowserPipHelper.PipHotkeyText` in FluentFlyout to match.

## Usage

| Action | Result |
| --- | --- |
| `Ctrl+Shift+6` (global) | The playing video enters picture-in-picture; an existing window is left cleanly. FluentFlyout uses this. |
| Toolbar button | Same, while the browser has focus. |
| FluentFlyout taskbar widget | Closes an open picture-in-picture window (playback continues), or asks this extension to open one. |

The badge says what happened: `PiP` entered · `off` left · `arm` the browser's own automatic entry is registered
(switch to another tab or window and it opens by itself) · `-` this page is not playing a video · `!` failed.
Hovering the icon shows the same plus the reason.

## Why an extension is needed

Chromium gives other programs no way in: there is no command line switch, window message or COM interface to
enter picture-in-picture, and video sites (YouTube, Bilibili, ...) replace the browser context menu with their
own, so simulating a user cannot reach the entry either. **Closing** is possible from outside (the small window
is an ordinary top level window and accepts `WM_CLOSE`), but Chromium then pauses the media — it treats it as the
video being taken away — which is why FluentFlyout asks this extension first, and only starts the playback again
through the media session when the window had to be closed directly.

## Known limits

* Chromium only lets a video enter picture-in-picture right after a **user gesture**
  (`NotAllowedError: Must be handling a user gesture if there isn't already an element in Picture-in-Picture`),
  and an injected script cannot provide one. The extension therefore also registers the media session action
  handler for `"enterpictureinpicture"`: from Chrome 134 the **browser** then opens the corner window by itself,
  and it does that without a gesture. That entry waits for a page to go from visible to hidden, so a page that is
  already in the background is pushed through that change with a very short tab switch.
* Audio and video cannot be told apart through the media session: PotPlayer reports `Music` for `.mp4` files and
  Edge reports `Music` for YouTube/Bilibili videos. The reliable check here is "does this page have a playing
  `<video>`".
* Sites that disable picture-in-picture (`disablepictureinpicture`, for example Netflix) and DRM protected media
  cannot be switched, and the media has to live in the top frame.

## Troubleshooting

The extension keeps its last runs in `chrome.storage.local` (`pipLog`), so a click that did nothing can be
explained afterwards: `entered`, `entered-after-tab-switch`, `armed`, `no-video`, `injection-empty` (an injected
function returned nothing — it must be self contained, it cannot call other functions of `background.js`) or
`injection-failed`. The FluentFlyout repository also ships `tools/hotkey-availability.ps1` (which global
combinations are free) and `tools/hotkey-diag.ps1` (whether a combination can be triggered at all).
