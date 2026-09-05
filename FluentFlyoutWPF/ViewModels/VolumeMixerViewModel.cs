// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentFlyout.Classes.Utils;
using FluentFlyoutWPF.Classes;
using FluentFlyoutWPF.Models;
using Microsoft.Win32;
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
    public event EventHandler? SessionVolumeChanged;

    public VolumeMixerViewModel()
    {
        DeviceName = string.Empty;
        AudioDeviceMonitor.Instance.DefaultDeviceChanged += OnDefaultDeviceChanged;
        TryRegisterSystemEvents();

        AttachDevice(AudioDeviceMonitor.Instance.GetDefaultRenderDevice());

        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
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
            ClearSessions();
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

    private void TryRegisterSystemEvents()
    {
        try
        {
            SystemEvents.SessionSwitch += OnSessionSwitch;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Failed to register SystemEvents handlers for volume mixer recovery");
        }
    }

    private void TryUnregisterSystemEvents()
    {
        try
        {
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Failed to unregister SystemEvents handlers for volume mixer recovery");
        }
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionUnlock || e.Reason == SessionSwitchReason.SessionLogon)
            RecoverAudioDeviceAfterResume($"session switch: {e.Reason}");
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
            RecoverAudioDeviceAfterResume("power resume");
    }

    private void RecoverAudioDeviceAfterResume(string reason)
    {
        // S3 resume invalidates the cached MMDevice (volume flyout frozen, mixer
        // showing a single stale session) and often fires no DefaultDeviceChanged
        // on desktops, so re-resolve the default endpoint after the audio stack settles.
        Logger.Info($"Reattaching volume mixer after resume ({reason})");
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(2000); } catch { }
            try
            {
                var app = System.Windows.Application.Current;
                if (app?.Dispatcher == null)
                    return;
                await app.Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        AttachDevice(AudioDeviceMonitor.Instance.GetDefaultRenderDevice());
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "Volume mixer resume reattach failed");
                    }
                });
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Volume mixer resume recovery failed");
            }
        });
    }

    private void OnSessionVolumeChanged(object? sender, EventArgs e)
    {
        SessionVolumeChanged?.Invoke(sender, e);
    }

    private void ClearSessions()
    {
        foreach (var session in Sessions)
            session.VolumeChanged -= OnSessionVolumeChanged;

        Sessions.Clear();
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

    public bool TryAdjustMasterVolume(float delta)
    {
        if (_device == null) return false;

        MasterVolume = Math.Clamp(MasterVolume + delta, 0f, 1f);
        return true;
    }

    public bool TryAdjustSessionVolume(int processId, float delta)
    {
        var session = Sessions.FirstOrDefault(session => session.ProcessId == processId);

        if (session == null)
        {
            RefreshSessions();
            session = Sessions.FirstOrDefault(session => session.ProcessId == processId);
        }

        if (session == null) return false;

        session.AdjustVolume(delta);
        return true;
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
        ClearSessions();

        if (_device == null)
            return;

        try
        {
            // update device reference because previous _device doesn't have updated sessions
            var updatedDevice = AudioDeviceMonitor.Instance.GetDeviceById(_device.ID) ?? _device;
            var sessionManager = updatedDevice.AudioSessionManager;
            var sessions = sessionManager.Sessions;

            // Apps that open several WASAPI sessions (or several processes of the
            // same executable, e.g. Flow Launcher) previously produced one row per
            // session. The native Windows mixer groups them per app, so keep only
            // the first session of each process id / display name (#973).
            var seenProcessIds = new HashSet<int>();
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < sessions.Count; i++)
            {
                var session = sessions[i];
                AudioSessionState sessionState = session.State;
                if (sessionState == AudioSessionState.AudioSessionStateExpired) continue;

                int pid = (int)session.GetProcessID;

                string name = pid != 0 ? GetSessionDisplayName(session) : "System sounds";

                if (name == "FluentFlyout") continue;

                // pid 0 is "System sounds", which is a single logical entry
                if (pid != 0 && !seenProcessIds.Add(pid)) continue;
                if (!seenNames.Add(name)) continue;

                var icon = MediaPlayerData.GetAndCacheProcessIcon(pid, name);
                var audioSession = new AudioSessionModel(session, name, pid, sessionState, icon);
                audioSession.VolumeChanged += OnSessionVolumeChanged;
                Sessions.Add(audioSession);
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
                // Resolve via the exe path: FileVersionInfo reads the file, so it
                // works for elevated/admin processes whose MainModule denies
                // access to a non-elevated caller (previously: "Unknown").
                string? path = null;
                try
                {
                    path = MediaPlayerData.TryGetProcessPath((int)pid);
                }
                catch
                {
                    // fall through to the ProcessName fallback below
                }

                if (!string.IsNullOrWhiteSpace(path))
                {
                    try
                    {
                        var versionInfo = FileVersionInfo.GetVersionInfo(path);
                        if (!string.IsNullOrWhiteSpace(versionInfo.FileDescription))
                            return versionInfo.FileDescription;
                    }
                    catch
                    {
                        // unreadable version resource, use process name below
                    }

                    try
                    {
                        return System.IO.Path.GetFileNameWithoutExtension(path);
                    }
                    catch
                    {
                        // fall through
                    }
                }

                try
                {
                    using var process = Process.GetProcessById((int)pid);
                    return process.MainWindowTitle is { Length: > 0 } title
                        ? title
                        : process.ProcessName;
                }
                catch
                {
                    // Process may have exited
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
            try
            {
                session.SyncFromDevice();
                //Logger.Trace("Session '{0}' (PID {1}) - Volume: {2}, Muted: {3}, State: {4}",
                //    session.DisplayName, session.ProcessId, session.Volume, session.IsMuted, session.State);
            }
            catch (Exception ex)
            {
                // A single dead session (process exited mid-poll) must not break
                // the mixer refresh, let alone the flyout loop calling us.
                Logger.Debug(ex, "Failed to sync session '{0}' (PID {1})", session.DisplayName, session.ProcessId);
            }
        }
    }


    public void Dispose()
    {
        if (_pollTimer != null)
        {
            _pollTimer.Tick -= OnPollTick;
            _pollTimer.Stop();
            _pollTimer = null;
        }

        AudioDeviceMonitor.Instance.DefaultDeviceChanged -= OnDefaultDeviceChanged;
        TryUnregisterSystemEvents();

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

        ClearSessions();

        GC.SuppressFinalize(this);
    }
}