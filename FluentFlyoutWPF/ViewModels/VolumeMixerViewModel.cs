// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentFlyout.Classes.Utils;
using FluentFlyoutWPF.Classes;
using FluentFlyoutWPF.Models;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Threading;

namespace FluentFlyoutWPF.ViewModels;

/// <summary>
/// ViewModel for the volume mixer, exposing master volume and per-application audio sessions.
/// </summary>
public partial class VolumeMixerViewModel : ObservableObject, IDisposable
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private MMDevice? _device;
    private DispatcherTimer? _pollTimer;
    private bool _suppressDevicePush;

    [ObservableProperty]
    public partial float MasterVolume { get; set; }

    [ObservableProperty]
    public partial bool IsMasterMuted { get; set; }

    [ObservableProperty]
    public partial string DeviceName { get; set; }

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    public ObservableCollection<AudioSessionModel> Sessions { get; } = [];

    public VolumeMixerViewModel()
    {
        DeviceName = string.Empty;
        AudioDeviceMonitor.Instance.DefaultDeviceChanged += OnDefaultDeviceChanged;

        AttachDevice(AudioDeviceMonitor.Instance.GetDefaultRenderDevice());

        // slow polling to detect changes just in case
        //_pollTimer = new DispatcherTimer
        //{
        //    Interval = TimeSpan.FromMilliseconds(5000)
        //};
        //_pollTimer.Tick += OnPollTick;
        //_pollTimer.Start();
    }

    partial void OnIsExpandedChanged(bool oldValue, bool newValue)
    {
        if (oldValue == newValue) return;
        RefreshSessions();
    }

    private void AttachDevice(MMDevice? device)
    {
        if (_device != null)
        {
            try
            {
                _device.AudioEndpointVolume.OnVolumeNotification -= OnEndpointVolumeNotification;
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "Failed to unsubscribe from previous volume notifications");
            }
        }

        _device = device;

        if (_device == null)
        {
            DeviceName = string.Empty;
            try
            {
                _suppressDevicePush = true;
                MasterVolume = 0f;
                IsMasterMuted = false;
            }
            finally
            {
                _suppressDevicePush = false;
            }
            Sessions.Clear();
            return;
        }

        try
        {
            _device.AudioEndpointVolume.OnVolumeNotification += OnEndpointVolumeNotification;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to subscribe to volume notifications");
        }

        try
        {
            DeviceName = _device.FriendlyName;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to read device friendly name");
            DeviceName = string.Empty;
        }
        SyncMasterFromDevice();
        RefreshSessions();
    }

    private void OnEndpointVolumeNotification(AudioVolumeNotificationData data)
    {
        try
        {
            var app = System.Windows.Application.Current;
            if (app?.Dispatcher == null)
                return;

            float vol = data.MasterVolume;
            bool mute = data.Muted;

            if (!app.Dispatcher.CheckAccess())
            {
                _ = app.Dispatcher.InvokeAsync(() => ApplyVolumeFromDevice(vol, mute));
            }
            else
            {
                ApplyVolumeFromDevice(vol, mute);
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to handle volume notification");
        }
    }

    private void ApplyVolumeFromDevice(float vol, bool mute)
    {
        _suppressDevicePush = true;
        try
        {
            vol = Math.Clamp(vol, 0f, 1f);
            if (MathF.Abs(MasterVolume - vol) > 0.001f)
                MasterVolume = vol;

            if (IsMasterMuted != mute)
                IsMasterMuted = mute;
        }
        finally
        {
            _suppressDevicePush = false;
        }
    }

    private void OnDefaultDeviceChanged(object? sender, DefaultDeviceChangedEventArgs e)
    {
        Logger.Info("Default render device changed, reattaching volume mixer");

        System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            AttachDevice(AudioDeviceMonitor.Instance.GetDeviceById(e.DeviceId));
        });
    }


    [RelayCommand]
    private void ToggleMasterMute() => IsMasterMuted = !IsMasterMuted;

    [RelayCommand]
    private void OpenVolumeMixer() => IsExpanded = !IsExpanded;

    partial void OnMasterVolumeChanged(float value)
    {
        if (_suppressDevicePush) return;
        if (_device == null) return;
        try
        {
            _device.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(value, 0f, 1f);
            if (MasterVolume == 0f)
            {
                IsMasterMuted = true;
            }
            else
            {
                IsMasterMuted = false;
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to set master volume on device");
        }
    }

    partial void OnIsMasterMutedChanged(bool value)
    {
        if (_suppressDevicePush) return;
        if (_device == null) return;
        try
        {
            _device.AudioEndpointVolume.Mute = value;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to set master mute on device");
        }
    }


    public void SyncMasterFromDevice()
    {
        if (_device == null) return;

        try
        {
            var app = System.Windows.Application.Current;
            if (app?.Dispatcher != null && !app.Dispatcher.CheckAccess())
            {
                _ = app.Dispatcher.InvokeAsync(SyncMasterFromDevice);
                return;
            }

            float vol;
            bool mute;
            try
            {
                vol = _device.AudioEndpointVolume.MasterVolumeLevelScalar;
                mute = _device.AudioEndpointVolume.Mute;
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "Failed to read master volume from device");
                return;
            }

            ApplyVolumeFromDevice(vol, mute);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to sync master volume from device");
        }
    }


    [RelayCommand]
    public void RefreshSessions()
    {
        Sessions.Clear();

        if (_device == null)
            return;

        try
        {
            // update device reference because previous _device doesn't have updated sessions
            var updatedDevice = AudioDeviceMonitor.Instance.GetDeviceById(_device.ID) ?? _device;
            var sessionManager = updatedDevice.AudioSessionManager;
            var sessions = sessionManager.Sessions;

            for (int i = 0; i < sessions.Count; i++)
            {
                var session = sessions[i];
                AudioSessionState sessionState = session.State;
                if (sessionState == AudioSessionState.AudioSessionStateExpired) continue;

                int pid = (int)session.GetProcessID;

                string name = pid != 0 ? GetSessionDisplayName(session) : "System sounds";

                if (name == "FluentFlyout") continue;

                var icon = MediaPlayerData.GetAndCacheProcessIcon(pid, name);
                Sessions.Add(new AudioSessionModel(session, name, pid, sessionState, icon));
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to enumerate audio sessions");
        }
    }

    private static string GetSessionDisplayName(AudioSessionControl session)
    {
        if (!string.IsNullOrWhiteSpace(session.DisplayName))
            return session.DisplayName;

        try
        {
            uint pid = session.GetProcessID;
            if (pid != 0)
            {
                var process = Process.GetProcessById((int)pid);
                var mainModule = process.MainModule;

                if (mainModule != null)
                {
                    return !string.IsNullOrWhiteSpace(mainModule.FileVersionInfo.FileDescription)
                    ? mainModule.FileVersionInfo.FileDescription
                    : process.MainWindowTitle;
                }
                else
                {
                    return process.MainWindowTitle is { Length: > 0 } title
                    ? title
                    : process.ProcessName;
                }
            }
        }
        catch
        {
            // Process may have exited
        }

        return "Unknown";
    }


    public void OnPollTick(object? sender, EventArgs e)
    {
        SyncMasterFromDevice();

        foreach (var session in Sessions)
        {
            session.SyncFromDevice();
            //Logger.Trace("Session '{0}' (PID {1}) - Volume: {2}, Muted: {3}, State: {4}",
            //    session.DisplayName, session.ProcessId, session.Volume, session.IsMuted, session.State);
        }
    }


    public void Dispose()
    {
        _pollTimer?.Stop();
        _pollTimer = null;

        AudioDeviceMonitor.Instance.DefaultDeviceChanged -= OnDefaultDeviceChanged;

        if (_device != null)
        {
            try
            {
                _device.AudioEndpointVolume.OnVolumeNotification -= OnEndpointVolumeNotification;
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "Failed to unsubscribe from volume notifications on dispose");
            }
        }

        Sessions.Clear();

        GC.SuppressFinalize(this);
    }
}