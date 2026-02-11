// Copyright © 2024-2026 The FluentFlyout Authors
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

        _pollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _pollTimer.Tick += OnPollTick;
        _pollTimer.Start();
    }

    partial void OnIsExpandedChanged(bool oldValue, bool newValue)
    {
        if (oldValue == newValue) return;
        RefreshSessions();
    }

    private void AttachDevice(MMDevice? device)
    {
        _device = device;

        if (_device == null)
        {
            DeviceName = string.Empty;
            MasterVolume = 0f;
            IsMasterMuted = false;
            Sessions.Clear();
            return;
        }

        DeviceName = _device.FriendlyName;
        SyncMasterFromDevice();
        RefreshSessions();
    }

    private void OnDefaultDeviceChanged(object? sender, DefaultDeviceChangedEventArgs e)
    {
        if (e.DataFlow != DataFlow.Render || e.Role != Role.Multimedia)
            return;

        Logger.Info("Default render device changed, reattaching volume mixer");

        System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            AttachDevice(AudioDeviceMonitor.Instance.GetDefaultRenderDevice());
        });
    }


    [RelayCommand]
    private void ToggleMasterMute() => IsMasterMuted = !IsMasterMuted;

    [RelayCommand]
    private void OpenVolumeMixer() => IsExpanded = !IsExpanded;

    partial void OnMasterVolumeChanged(float value)
    {
        if (_device == null) return;
        _device.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(value, 0f, 1f);
        if (MasterVolume == 0f)
        {
            IsMasterMuted = true;
        }
    }

    partial void OnIsMasterMutedChanged(bool value)
    {
        if (_device == null) return;
        _device.AudioEndpointVolume.Mute = value;
    }


    private void SyncMasterFromDevice()
    {
        if (_device == null) return;

        var vol = _device.AudioEndpointVolume.MasterVolumeLevelScalar;
        var mute = _device.AudioEndpointVolume.Mute;

        if (MathF.Abs(MasterVolume - vol) > 0.001f)
            MasterVolume = vol;

        if (IsMasterMuted != mute)
            IsMasterMuted = mute;
    }


    [RelayCommand]
    public void RefreshSessions()
    {
        Sessions.Clear();

        if (_device == null)
            return;

        try
        {
            var sessionManager = _device.AudioSessionManager;
            var sessions = sessionManager.Sessions;

            for (int i = 0; i < sessions.Count; i++)
            {
                var session = sessions[i];
                AudioSessionState sessionState = session.State;
                if (sessionState == AudioSessionState.AudioSessionStateExpired) continue;

                int pid = (int)session.GetProcessID;

                string name = pid != 0 ? GetSessionDisplayName(session) : "System sounds";

                if (name == "FluentFlyout") continue;

                var icon = MediaPlayerData.GetProcessIcon(pid, name);
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
                return process.MainWindowTitle is { Length: > 0 } title
                    ? title
                    : process.ProcessName;
            }
        }
        catch
        {
            // Process may have exited
        }

        return "Unknown";
    }


    private void OnPollTick(object? sender, EventArgs e)
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

        Sessions.Clear();

        GC.SuppressFinalize(this);
    }
}
