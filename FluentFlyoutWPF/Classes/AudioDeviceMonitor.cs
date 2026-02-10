// Copyright © 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace FluentFlyoutWPF.Classes
{
    public class AudioDeviceMonitor : IDisposable
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
        private static AudioDeviceMonitor? _instance;
        private static readonly object _instanceLock = new();

        private MMDeviceEnumerator? _deviceEnumerator;
        private AudioDeviceNotificationClient? _notificationClient;

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
                _notificationClient = new AudioDeviceNotificationClient();
                _notificationClient.DefaultDeviceChanged += OnDefaultDeviceChanged;
                _deviceEnumerator.RegisterEndpointNotificationCallback(_notificationClient);
                
                Logger.Info("Audio device monitoring initialized");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to initialize audio device monitoring");
            }
        }

        private void OnDefaultDeviceChanged(object? sender, DefaultDeviceChangedEventArgs e)
        {
            DefaultDeviceChanged?.Invoke(this, e);
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
            if (_notificationClient != null)
            {
                _notificationClient.DefaultDeviceChanged -= OnDefaultDeviceChanged;
            }

            if (_deviceEnumerator != null && _notificationClient != null)
            {
                try
                {
                    _deviceEnumerator.UnregisterEndpointNotificationCallback(_notificationClient);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Failed to unregister device notification callback");
                }
                _deviceEnumerator = null;
            }

            _notificationClient = null;

            GC.SuppressFinalize(this);
        }
    }

    // classes to handle audio device notifications
    public class AudioDeviceNotificationClient : IMMNotificationClient
    {
        public event EventHandler<DefaultDeviceChangedEventArgs>? DefaultDeviceChanged;

        public void OnDeviceStateChanged(string deviceId, DeviceState newState)
        {
        }

        public void OnDeviceAdded(string pwstrDeviceId)
        {
        }

        public void OnDeviceRemoved(string deviceId)
        {
        }

        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            DefaultDeviceChanged?.Invoke(this, new DefaultDeviceChangedEventArgs(flow, role, defaultDeviceId));
        }

        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key)
        {
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
