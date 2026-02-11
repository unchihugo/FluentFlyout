// Copyright © 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using System.Windows.Media;

namespace FluentFlyoutWPF.Models;

public partial class AudioSessionModel : ObservableObject
{
    private readonly AudioSessionControl _sessionControl;

    [ObservableProperty]
    public partial string DisplayName { get; set; }

    [ObservableProperty]
    public partial int ProcessId { get; set; }

    [ObservableProperty]
    public partial AudioSessionState State { get; set; }

    [ObservableProperty]
    public partial float Volume { get; set; }

    [ObservableProperty]
    public partial bool IsMuted { get; set; }

    public ImageSource? Icon { get; }

    public AudioSessionModel(AudioSessionControl sessionControl, string displayName, int processId, AudioSessionState sessionState, ImageSource? icon)
    {
        _sessionControl = sessionControl;
        DisplayName = displayName;
        ProcessId = processId;
        State = sessionState;
        Icon = icon;
        Volume = _sessionControl.SimpleAudioVolume.Volume;
        IsMuted = _sessionControl.SimpleAudioVolume.Mute;
    }

    partial void OnVolumeChanged(float value)
    {
        _sessionControl.SimpleAudioVolume.Volume = Math.Clamp(value, 0f, 1f);
        if (Volume == 0f)
        {
            IsMuted = true;
        }
    }

    partial void OnIsMutedChanged(bool value)
    {
        _sessionControl.SimpleAudioVolume.Mute = value;
    }

    [RelayCommand]
    private void ToggleMute() => IsMuted = !IsMuted;

    /// <summary>
    /// refreshes Volume and IsMuted from the audio session without pushing changes back
    /// </summary>
    public void SyncFromDevice()
    {
        var vol = _sessionControl.SimpleAudioVolume.Volume;
        var mute = _sessionControl.SimpleAudioVolume.Mute;

        if (MathF.Abs(Volume - vol) > 0.001f)
            Volume = vol;

        if (IsMuted != mute)
            IsMuted = mute;
    }
}
