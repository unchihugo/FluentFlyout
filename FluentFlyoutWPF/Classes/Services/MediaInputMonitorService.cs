// Copyright © 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

namespace FluentFlyoutWPF.Classes.Services;

/// <summary>
/// Identifies the source that triggered a media input event.
/// </summary>
public enum InputTrigger
{
    /// <summary>User pressed a keyboard key (media or volume).</summary>
    KeyboardHook,
}

/// <summary>
/// Data for a volume change event dispatched by <see cref="MediaInputMonitorService"/>.
/// </summary>
public class VolumeChangedEventArgs : EventArgs
{
    /// <summary>The source that triggered this volume change event.</summary>
    public InputTrigger Trigger { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="VolumeChangedEventArgs"/>.
    /// </summary>
    /// <param name="trigger">The source that triggered this volume change event.</param>
    public VolumeChangedEventArgs(InputTrigger trigger)
    {
        Trigger = trigger;
    }
}

/// <summary>
/// Data for a media key pressed event dispatched by <see cref="MediaInputMonitorService"/>.
/// </summary>
public class MediaKeyPressedEventArgs : EventArgs
{
    /// <summary>The source that triggered this media key event.</summary>
    public InputTrigger Trigger { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="MediaKeyPressedEventArgs"/>.
    /// </summary>
    /// <param name="trigger">The source that triggered this media key event.</param>
    public MediaKeyPressedEventArgs(InputTrigger trigger)
    {
        Trigger = trigger;
    }
}

/// <summary>
/// Dispatches media input events (volume key presses and media key presses) to subscribers,
/// decoupling input detection from flyout display logic.
/// Supports keyboard hook mode now; NAudio real-time monitoring can be added in a future mode.
/// </summary>
public class MediaInputMonitorService
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    /// <summary>Fired when a volume key (mute / volume up / volume down) is detected.</summary>
    public event EventHandler<VolumeChangedEventArgs>? VolumeChanged;

    /// <summary>Fired when a media key (play/pause, next, previous, stop) is detected.</summary>
    public event EventHandler<MediaKeyPressedEventArgs>? MediaKeyPressed;

    /// <summary>
    /// Called by the keyboard hook when a volume key is captured,
    /// dispatching the <see cref="VolumeChanged"/> event.
    /// </summary>
    public void NotifyKeyboardVolumeKey()
    {
        Logger.Debug("Volume key detected via keyboard hook");
        VolumeChanged?.Invoke(this, new VolumeChangedEventArgs(InputTrigger.KeyboardHook));
    }

    /// <summary>
    /// Called by the keyboard hook when a media key is captured,
    /// dispatching the <see cref="MediaKeyPressed"/> event.
    /// </summary>
    public void NotifyKeyboardMediaKey()
    {
        Logger.Debug("Media key detected via keyboard hook");
        MediaKeyPressed?.Invoke(this, new MediaKeyPressedEventArgs(InputTrigger.KeyboardHook));
    }
}
