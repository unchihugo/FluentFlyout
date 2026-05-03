// Copyright (c) 2024-2026 The FluentFlyout Authors
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
            // Windows fires this for every Role (Console, Multimedia, Communications).
            // Only forward Render/Multimedia to avoid triple-firing on a single switch.
            if (e.DataFlow != DataFlow.Render)
                return;

            Logger.Info("Default audio output device changed");
            DefaultDeviceChanged?.Invoke(this, e);
        }

        public MMDevice? GetDefaultRenderDevice()
        {
            try
            {
                var device = _deviceEnumerator?.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                Logger.Debug("New device: {0}", device);
                return device;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to get default render device");
                return null;
            }
        }

        public MMDevice? GetDeviceById(string deviceId)
        {
            try
            {
                var device = _deviceEnumerator?.GetDevice(deviceId);
                Logger.Debug("Got device by id {0}: {1}", deviceId, device);
                return device;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to get device by id {0}", deviceId);
                return null;
            }
        }

        public void Dispose()
        {
            _notificationClient?.DefaultDeviceChanged -= OnDefaultDeviceChanged;

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

        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            DefaultDeviceChanged?.Invoke(this, new DefaultDeviceChangedEventArgs(flow, role, defaultDeviceId));
        }

        public void OnDeviceStateChanged(string deviceId, DeviceState newState) { }
        public void OnDeviceAdded(string pwstrDeviceId) { }
        public void OnDeviceRemoved(string deviceId) { }
        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
    }

    public class DefaultDeviceChangedEventArgs(DataFlow dataFlow, Role role, string deviceId) : EventArgs
    {
        public DataFlow DataFlow { get; } = dataFlow;
        public Role Role { get; } = role;
        public string DeviceId { get; } = deviceId;
    }
}