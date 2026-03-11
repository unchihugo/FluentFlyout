// Copyright © 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Runtime.InteropServices;
using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF.Classes;
using FluentFlyoutWPF.Services.Args;
using NAudio.CoreAudioApi;

namespace FluentFlyoutWPF.Services;

public sealed partial class InputMonitorService
{
    private readonly Lock _nAudioSyncRoot = new();
    private MMDevice? _nAudioRenderDevice;
    private string? _nAudioRenderDeviceId;
    private bool _nAudioDeviceChangeSubscribed;


    /// <summary>
    /// Rebinds On Default Render Device Changed
    /// </summary>
    private void OnDefaultRenderDeviceChanged(object? sender, DefaultDeviceChangedEventArgs e)
    {
        if (e.DataFlow != DataFlow.Render)
        {
            return;
        }


        if (!IsNAudioMonitoringActive())
        {
            return;
        }

        _ = Task.Run(BindNRenderDevice);
    }

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
    private bool BindNRenderDevice()
    {
        MMDevice? nextDevice = AudioDeviceMonitor.Instance.GetDefaultRenderDevice();
        if (nextDevice == null)
        {
            return false;
        }

        string nextDeviceId = nextDevice.ID;
        if (_nAudioRenderDevice != null &&
            string.Equals(_nAudioRenderDeviceId, nextDeviceId, StringComparison.OrdinalIgnoreCase))
        {
            nextDevice.Dispose();
            return true;
        }

        UnbindNRenderDevice();

        _nAudioRenderDevice = nextDevice;
        _nAudioRenderDeviceId = nextDeviceId;
        try
        {
            _nAudioRenderDevice.AudioEndpointVolume.OnVolumeNotification += OnNVolumeNotification;
        }
        catch (InvalidComObjectException)
        {
            // The audio device failed during the binding process.
            Logger.Warn("Binding NAudio render device failed about COM: {}", nextDeviceId);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Unsubscribes endpoint volume notifications from the bound render device and disposes it.
    /// </summary>
    private void UnbindNRenderDevice()
    {
        _nAudioRenderDevice = null;
        _nAudioRenderDeviceId = null;

        if (_nAudioRenderDevice == null)
        {
            return;
        }

        try
        {
            try
            {
                _nAudioRenderDevice.AudioEndpointVolume.OnVolumeNotification -= OnNVolumeNotification;
            }
            catch (InvalidComObjectException)
            {
                // ignore
            }

            try
            {
                _nAudioRenderDevice.Dispose();
            }
            catch (InvalidComObjectException)
            {
                // ignore
            }
        }
        catch (Exception)
        {
            // ignore
        }
        finally
        {
            _nAudioRenderDevice = null;
        }
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

    private bool IsNAudioMonitoringActive()
    {
        lock (_syncRoot)
        {
            return _isStarted && SettingsManager.Current.MediaFlyoutInputSource == InputMonitorTrigger.N_AUDIO;
        }
    }
}