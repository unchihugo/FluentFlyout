// Copyright © 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;
using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF.Services.Args;
using static FluentFlyout.Classes.NativeMethods;

namespace FluentFlyoutWPF.Services;

/// <summary>
/// Identifies the source that triggered an input monitor event.
/// </summary>
public enum InputMonitorTrigger
{
    /// <summary>User pressed a keyboard key (media, volume, or lock key).</summary>
    KEYBOARD_HOOK,

    /// <summary>Audio endpoint volume changed and was detected through NAudio (volume up/down/mute only, no playback impact).</summary>
    N_AUDIO,
}


/// <summary>
/// Identifies which lock key generated an input event.
/// </summary>
public enum LockKeyType
{
    /// <summary>Caps Lock key.</summary>
    CAPS_LOCK,

    /// <summary>Num Lock key.</summary>
    NUM_LOCK,

    /// <summary>Scroll Lock key.</summary>
    SCROLL_LOCK,

    /// <summary>Insert key.</summary>
    INSERT,
}

 

/// <summary>
/// Monitors system input sources and dispatches normalized events for media flyout related interactions.
/// Currently uses a global low-level keyboard hook source and is designed for future source expansion.
/// </summary>
public sealed partial class InputMonitorService : IDisposable
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    private static readonly Lazy<InputMonitorService> LazyInstance = new(() => new InputMonitorService());

    private readonly Lock _syncRoot = new();
    private readonly ConcurrentQueue<Action> _dispatchQueue = new();
    private readonly SemaphoreSlim _dispatchSignal = new(0);
    private readonly CancellationTokenSource _dispatchCts = new();
    private readonly Thread _dispatchThread;

    private IntPtr _hookId = IntPtr.Zero;
    private LowLevelKeyboardProc? _hookProc;
    private long _lastFlyoutTime;
    private long _lastLockKeyEventTime;

    private bool _isStarted;

    private const int LOCK_KEY_THROTTLE_MS = 120;
    /// <summary>
    /// Gets the global singleton instance.
    /// </summary>
    public static InputMonitorService Instance => LazyInstance.Value;

    /// <summary>Fired when a volume key (mute / volume up / volume down) is detected.</summary>
    public event EventHandler<VolumeChangedEventArgs>? VolumeChanged;

    /// <summary>Fired when a media key (play/pause, next, previous, stop) is detected.</summary>
    public event EventHandler<MediaKeyPressedEventArgs>? MediaKeyPressed;

    /// <summary>Fired when a lock key (CapsLock / NumLock / ScrollLock / Insert) is detected.</summary>
    public event EventHandler<LockKeyPressedEventArgs>? LockKeyPressed;

    private InputMonitorService()
    {
        _dispatchThread = new Thread(ProcessDispatchQueue)
        {
            IsBackground = true,
            Name = "InputMonitorService.Dispatcher"
        };
        _dispatchThread.Start();
    }

    /// <summary>
    /// Starts input monitoring for all currently enabled sources.
    /// </summary>
    public void Start()
    {
        lock (_syncRoot)
        {
            if (_isStarted)
            {
                return;
            }

            _hookProc = HookCallback;
            _hookId = SetHook(_hookProc);
            _isStarted = _hookId != IntPtr.Zero;

            if (!_isStarted)
            {
                Logger.Warn("InputMonitorService failed to install keyboard hook");
            }

            RefreshSourceConfiguration();
        }
    }

    /// <summary>
    /// Stops input monitoring and releases active source resources.
    /// </summary>
    public void Stop()
    {
        lock (_syncRoot)
        {
            if (!_isStarted)
            {
                return;
            }

            if (_hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
            }

            // Keep delegate alive until after the hook is removed.
            if (_hookProc != null)
            {
                GC.KeepAlive(_hookProc);
            }

            _hookProc = null;
            _isStarted = false;

            StopNAudioMonitoring();
        }
    }

    /// <summary>
    /// Re-applies source-specific monitoring according to current settings.
    /// </summary>
    public void RefreshSourceConfiguration()
    {
        lock (_syncRoot)
        {
            if (!_isStarted)
            {
                return;
            }

            if (SettingsManager.Current.MediaFlyoutInputSource == InputMonitorTrigger.N_AUDIO)
            {
                StartNAudioMonitoring();
            }
            else
            {
                StopNAudioMonitoring();
            }
        }
    }

    /// <summary>
    /// Releases monitor resources.
    /// </summary>
    public void Dispose()
    {
        Stop();
        _dispatchCts.Cancel();
        _dispatchSignal.Release();
        _dispatchSignal.Dispose();
        _dispatchCts.Dispose();
    }

    private static IntPtr SetHook(LowLevelKeyboardProc proc)
    {
        using Process curProcess = Process.GetCurrentProcess();
        using ProcessModule? curModule = curProcess.MainModule;
        return curModule == null ? IntPtr.Zero : SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(curModule.ModuleName), 0);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        bool isKeyDown = wParam == WM_KEYDOWN;
        bool isKeyUp = wParam == WM_KEYUP;
        if (nCode < 0 || (!isKeyDown && !isKeyUp))
        {
            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        int vkCode = Marshal.ReadInt32(lParam);

        bool mediaKeyPressed = vkCode is MEDIA_KEY_PLAY_PAUSE or MEDIA_KEY_NEXT_TRACK or MEDIA_KEY_PREVIOUS_TRACK or MEDIA_KEY_STOP;
        bool volumeKeyPressed = vkCode is VOLUME_KEY_MUTE or VOLUME_KEY_DOWN or VOLUME_KEY_UP;

        bool useKeyboardHookForFlyout = SettingsManager.Current.MediaFlyoutInputSource == InputMonitorTrigger.KEYBOARD_HOOK;

        // 1. If it is mediaKeyPressed, it will enter regardless of the mode.
        // 2. If it is volumeKeyPressed, it can only enter when the Hook mode is enabled and volumeKeyPressed is not excluded.
        bool shouldProcess = mediaKeyPressed || 
                            (useKeyboardHookForFlyout && !SettingsManager.Current.MediaFlyoutVolumeKeysExcluded && volumeKeyPressed);

        if (shouldProcess)
        {
            long currentTime = Environment.TickCount64;
            long lastFlyoutTime = Interlocked.Read(ref _lastFlyoutTime);
            
            if ((currentTime - lastFlyoutTime) >= 500)
            {
                Interlocked.Exchange(ref _lastFlyoutTime, currentTime);

                if (volumeKeyPressed)
                {
                    DispatchEventAsync(DispatchVolumeChanged);
                }
                else // mediaKeyPressed is true
                {
                    DispatchEventAsync(DispatchMediaKeyPressed);
                }
            }
        }

        // Emits lock key events separately so UI policies can be applied by subscribers.
        switch (vkCode)
        {
            case VK_CAPS_LOCK:
                RaiseLockKeyPressed(LockKeyType.CAPS_LOCK, Keyboard.IsKeyToggled(Key.CapsLock));
                break;
            case VK_NUM_LOCK:
                RaiseLockKeyPressed(LockKeyType.NUM_LOCK, Keyboard.IsKeyToggled(Key.NumLock));
                break;
            case VK_SCROLL_LOCK:
                RaiseLockKeyPressed(LockKeyType.SCROLL_LOCK, Keyboard.IsKeyToggled(Key.Scroll));
                break;
            case VK_INSERT:
                RaiseLockKeyPressed(LockKeyType.INSERT, Keyboard.IsKeyToggled(Key.Insert));
                break;
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private void RaiseLockKeyPressed(LockKeyType keyType, bool isToggled)
    {
        long currentTime = Environment.TickCount64;
        long lastLockKeyEventTime = Interlocked.Read(ref _lastLockKeyEventTime);
        if ((currentTime - lastLockKeyEventTime) < LOCK_KEY_THROTTLE_MS)
        {
            return;
        }

        Interlocked.Exchange(ref _lastLockKeyEventTime, currentTime);
        DispatchEventAsync(() => LockKeyPressed?.Invoke(this, new LockKeyPressedEventArgs(InputMonitorTrigger.KEYBOARD_HOOK, keyType, isToggled)));
    }

    private void DispatchEventAsync(Action action)
    {
        _dispatchQueue.Enqueue(action);
        _dispatchSignal.Release();
    }

    private void DispatchVolumeChanged()
    {
        Logger.Debug("Volume key detected via keyboard hook");
        VolumeChanged?.Invoke(this, new VolumeChangedEventArgs(InputMonitorTrigger.KEYBOARD_HOOK));
    }

    private void DispatchMediaKeyPressed()
    {
        Logger.Debug("Media key detected via keyboard hook");
        MediaKeyPressed?.Invoke(this, new MediaKeyPressedEventArgs(InputMonitorTrigger.KEYBOARD_HOOK));
    }

    
    private void ProcessDispatchQueue()
    {
        CancellationToken token = _dispatchCts.Token;
        try
        {
            while (true)
            {
                _dispatchSignal.Wait(token);

                while (_dispatchQueue.TryDequeue(out Action? action))
                {
                    try
                    {
                        action();
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "InputMonitorService event dispatch failed");
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown path.
        }
    }

}
