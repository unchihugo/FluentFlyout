// Copyright © 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF.Classes;
using FluentFlyoutWPF.Services.Args;
using NAudio.CoreAudioApi;

namespace FluentFlyoutWPF.Services;

public sealed partial class InputMonitorService
{
    private readonly Lock _nAudioSyncRoot = new();
    private MMDevice? _nAudioRenderDevice;
    private bool _nAudioDeviceChangeSubscribed;

    /// <summary>
    /// Starts NAudio-based monitoring by subscribing to default render device changes
    /// and binding volume notifications from the current default render device.
    /// </summary>
    private void StartNAudioMonitoring()
    {
        lock (_nAudioSyncRoot)
        {
            if (!_nAudioDeviceChangeSubscribed)
            {
                AudioDeviceMonitor.Instance.DefaultDeviceChanged += OnDefaultRenderDeviceChanged;
                _nAudioDeviceChangeSubscribed = true;
            }

            BindNRenderDevice();
        }
    }

    /// <summary>
    /// Stops NAudio-based monitoring and releases related subscriptions/resources.
    /// </summary>
    private void StopNAudioMonitoring()
    {
        lock (_nAudioSyncRoot)
        {
            if (_nAudioDeviceChangeSubscribed)
            {
                AudioDeviceMonitor.Instance.DefaultDeviceChanged -= OnDefaultRenderDeviceChanged;
                _nAudioDeviceChangeSubscribed = false;
            }

            UnbindNRenderDevice();
        }
    }

    /// <summary>
    /// Rebinds the current default render device and subscribes to endpoint volume notifications.
    /// </summary>
    private void BindNRenderDevice()
    {
        UnbindNRenderDevice();

        _nAudioRenderDevice = AudioDeviceMonitor.Instance.GetDefaultRenderDevice();
        if (_nAudioRenderDevice == null)
        {
            Logger.Warn("InputMonitorService failed to acquire default render device for NAudio source");
            return;
        }

        _nAudioRenderDevice.AudioEndpointVolume.OnVolumeNotification += OnNVolumeNotification;
    }

    /// <summary>
    /// Unsubscribes endpoint volume notifications from the bound render device and disposes it.
    /// </summary>
    private void UnbindNRenderDevice()
    {
        if (_nAudioRenderDevice == null)
        {
            return;
        }

        _nAudioRenderDevice.AudioEndpointVolume.OnVolumeNotification -= OnNVolumeNotification;
        _nAudioRenderDevice.Dispose();
        _nAudioRenderDevice = null;
    }

    /// <summary>
    /// Handles NAudio endpoint volume notifications and dispatches a volume change event
    /// when the monitor is active and the throttle interval has elapsed.
    /// </summary>
    /// <param name="data">The volume notification payload provided by NAudio.</param>
    private void OnNVolumeNotification(AudioVolumeNotificationData data)
    {
        lock (_syncRoot)
        {
            if (!_isStarted || SettingsManager.Current.MediaFlyoutInputSource != InputMonitorTrigger.N_AUDIO)
            {
                return;
            }
        }

        if (SettingsManager.Current.MediaFlyoutVolumeKeysExcluded)
        {
            return;
        }

        long currentTime = Environment.TickCount64;
        long lastFlyoutTime = Interlocked.Read(ref _lastFlyoutTime);
        if ((currentTime - lastFlyoutTime) < 500)
        {
            return;
        }

        Interlocked.Exchange(ref _lastFlyoutTime, currentTime);
        DispatchEventAsync(() => VolumeChanged?.Invoke(this, new VolumeChangedEventArgs(InputMonitorTrigger.N_AUDIO)));
    }

}