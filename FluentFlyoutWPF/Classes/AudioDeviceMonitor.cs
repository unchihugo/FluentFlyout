// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using NAudio.CoreAudioApi;

namespace FluentFlyoutWPF.Classes
{
    public class AudioDeviceMonitor : IDisposable
    {
        // TODO: implement OnDefaultDeviceChanged event - currently gets handled by Visualizer and VolumeMixerViewModel

        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
        private static AudioDeviceMonitor? _instance;
        private static readonly object _instanceLock = new();

        private MMDeviceEnumerator? _deviceEnumerator;

        public event EventHandler<DefaultDeviceChangedEventArgs>? DefaultDeviceChanged;

        public static AudioDeviceMonitor Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_instanceLock)
                    {
                        _instance ??= new AudioDeviceMonitor();
                    }
                }
                return _instance;
            }
        }

        private AudioDeviceMonitor()
        {
            Initialize();
        }

        private void Initialize()
        {
            try
            {
                _deviceEnumerator = new MMDeviceEnumerator();
                Logger.Info("Audio device monitoring initialized");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to initialize audio device monitoring");
            }
        }

        public MMDevice? GetDefaultRenderDevice()
        {
            try
            {
                return _deviceEnumerator?.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to get default render device");
                return null;
            }
        }

        public void Dispose()
        {
            _deviceEnumerator?.Dispose();
            GC.SuppressFinalize(this);
        }
    }

    public class DefaultDeviceChangedEventArgs : EventArgs
    {
        public DataFlow DataFlow { get; }
        public Role Role { get; }
        public string DeviceId { get; }

        public DefaultDeviceChangedEventArgs(DataFlow dataFlow, Role role, string deviceId)
        {
            DataFlow = dataFlow;
            Role = role;
            DeviceId = deviceId;
        }
    }
}