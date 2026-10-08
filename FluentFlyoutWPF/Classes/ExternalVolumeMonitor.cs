// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using NAudio.CoreAudioApi;

namespace FluentFlyoutWPF.Classes;

public class ExternalVolumeMonitor : IDisposable
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private readonly int _localInputGracePeriodMs;
    private MMDevice? _device;
    private float _lastVolume;
    private bool _lastMuted;

    public event EventHandler? ExternalVolumeChanged;

    public ExternalVolumeMonitor(int localInputGracePeriodMs)
    {
        _localInputGracePeriodMs = localInputGracePeriodMs;

        AudioDeviceMonitor.Instance.DefaultDeviceChanged += OnDefaultDeviceChanged;
        AttachDevice(AudioDeviceMonitor.Instance.GetDefaultRenderDevice());
    }

    private void AttachDevice(MMDevice? device)
    {
        DetachDevice();

        if (device == null)
            return;

        try
        {
            _lastVolume = device.AudioEndpointVolume.MasterVolumeLevelScalar;
            _lastMuted = device.AudioEndpointVolume.Mute;
            device.AudioEndpointVolume.OnVolumeNotification += OnVolumeNotification;
            _device = device;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to watch volume of device {0}", device.FriendlyName);
            device.Dispose();
        }
    }

    private void DetachDevice()
    {
        if (_device == null)
            return;

        try
        {
            _device.AudioEndpointVolume.OnVolumeNotification -= OnVolumeNotification;
            _device.Dispose();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to stop watching device volume");
        }
        finally
        {
            _device = null;
        }
    }

    private void OnDefaultDeviceChanged(object? sender, DefaultDeviceChangedEventArgs e)
    {
        System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            AttachDevice(AudioDeviceMonitor.Instance.GetDeviceById(e.DeviceId));
        });
    }

    private void OnVolumeNotification(AudioVolumeNotificationData data)
    {
        // the volume mixer writes the same volume back when syncing
        bool changed = MathF.Abs(data.MasterVolume - _lastVolume) > 0.001f || data.Muted != _lastMuted;
        _lastVolume = data.MasterVolume;
        _lastMuted = data.Muted;

        if (!changed || LocalInputDetector.HadInputWithin(_localInputGracePeriodMs))
            return;

#if DEBUG
        Logger.Debug("External volume change detected: {0:P0}, muted: {1}", data.MasterVolume, data.Muted);
#endif
        ExternalVolumeChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        AudioDeviceMonitor.Instance.DefaultDeviceChanged -= OnDefaultDeviceChanged;
        DetachDevice();

        GC.SuppressFinalize(this);
    }
}
