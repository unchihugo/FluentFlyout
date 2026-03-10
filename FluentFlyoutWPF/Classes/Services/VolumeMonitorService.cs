// Copyright © 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

namespace FluentFlyoutWPF.Classes.Services;

/// <summary>
/// Identifies the source that triggered a volume change event.
/// </summary>
public enum VolumeChangeTrigger
{
    /// <summary>User pressed a volume-related keyboard key (mute/volume up/volume down).</summary>
    KeyboardHook,
}

/// <summary>
/// Data for a volume change event.
/// </summary>
public class VolumeChangedEventArgs : EventArgs
{
    /// <summary>The source that triggered this volume change event.</summary>
    public VolumeChangeTrigger Trigger { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="VolumeChangedEventArgs"/>.
    /// </summary>
    /// <param name="trigger">The source that triggered this volume change event.</param>
    public VolumeChangedEventArgs(VolumeChangeTrigger trigger)
    {
        Trigger = trigger;
    }
}

/// <summary>
/// Responsible for distributing volume change events to subscribers.
/// Currently supports keyboard hook trigger mode, with potential future expansion to NAudio real-time monitoring mode.
/// </summary>
public class VolumeMonitorService
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    /// <summary>It is triggered when a change in volume is detected.</summary>
    public event EventHandler<VolumeChangedEventArgs>? VolumeChanged;

    /// <summary>
    /// Called by the keyboard hook when a volume key (mute/volume up/volume down) is detected, dispatching the <see cref="VolumeChanged"/> event.
    /// </summary>
    public void NotifyKeyboardVolumeKey()
    {
        Logger.Debug("Volume key detected via keyboard hook");
        VolumeChanged?.Invoke(this, new VolumeChangedEventArgs(VolumeChangeTrigger.KeyboardHook));
    }
}
