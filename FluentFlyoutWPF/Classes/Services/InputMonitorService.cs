// Copyright © 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes.Settings;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Runtime.InteropServices;
using System.Windows.Input;
using static FluentFlyout.Classes.NativeMethods;

namespace FluentFlyoutWPF.Classes.Services;

/// <summary>
/// Identifies the source that triggered an input monitor event.
/// </summary>
public enum InputTrigger
{
    /// <summary>User pressed a keyboard key (media, volume, or lock key).</summary>
    KeyboardHook,
}

/// <summary>
/// Identifies which lock key generated an input event.
/// </summary>
public enum LockKeyType
{
    /// <summary>Caps Lock key.</summary>
    CapsLock,

    /// <summary>Num Lock key.</summary>
    NumLock,

    /// <summary>Scroll Lock key.</summary>
    ScrollLock,

    /// <summary>Insert key.</summary>
    Insert,
}

/// <summary>
/// Data for a volume change event dispatched by <see cref="InputMonitorService"/>.
/// </summary>
public class VolumeChangedEventArgs : EventArgs
{
    /// <summary>The source that triggered this volume change event.</summary>
    public InputTrigger Trigger { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="VolumeChangedEventArgs"/>.
    /// </summary>
    /// <param name="trigger">The source that triggered this volume change event.</param>
    public VolumeChangedEventArgs(InputTrigger trigger)
    {
        Trigger = trigger;
    }
}

/// <summary>
/// Data for a media key pressed event dispatched by <see cref="InputMonitorService"/>.
/// </summary>
public class MediaKeyPressedEventArgs : EventArgs
{
    /// <summary>The source that triggered this media key event.</summary>
    public InputTrigger Trigger { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="MediaKeyPressedEventArgs"/>.
    /// </summary>
    /// <param name="trigger">The source that triggered this media key event.</param>
    public MediaKeyPressedEventArgs(InputTrigger trigger)
    {
        Trigger = trigger;
    }
}

/// <summary>
/// Data for a lock key event dispatched by <see cref="InputMonitorService"/>.
/// </summary>
public class LockKeyPressedEventArgs : EventArgs
{
    /// <summary>The source that triggered this lock key event.</summary>
    public InputTrigger Trigger { get; }

    /// <summary>The lock key that generated the event.</summary>
    public LockKeyType KeyType { get; }

    /// <summary>Current toggled state of the lock key after the key event.</summary>
    public bool IsToggled { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="LockKeyPressedEventArgs"/>.
    /// </summary>
    /// <param name="trigger">The source that triggered this lock key event.</param>
    /// <param name="keyType">The lock key that generated the event.</param>
    /// <param name="isToggled">Current toggled state of the lock key after the key event.</param>
    public LockKeyPressedEventArgs(InputTrigger trigger, LockKeyType keyType, bool isToggled)
    {
        Trigger = trigger;
        KeyType = keyType;
        IsToggled = isToggled;
    }
}

/// <summary>
/// Monitors system input sources and dispatches normalized events for media flyout related interactions.
/// Currently uses a global low-level keyboard hook source and is designed for future source expansion.
/// </summary>
public sealed class InputMonitorService : IDisposable
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    private static readonly Lazy<InputMonitorService> LazyInstance = new(() => new InputMonitorService());

    private readonly object _syncRoot = new();
    private readonly ConcurrentQueue<Action> _dispatchQueue = new();
    private readonly SemaphoreSlim _dispatchSignal = new(0);
    private readonly CancellationTokenSource _dispatchCts = new();
    private readonly Thread _dispatchThread;

    private IntPtr _hookId = IntPtr.Zero;
    private LowLevelKeyboardProc? _hookProc;
    private long _lastFlyoutTime;
    private long _lastLockKeyEventTime;
    private bool _isStarted;

    private const int MediaKeyPlayPause = 0xB3;
    private const int MediaKeyNextTrack = 0xB0;
    private const int MediaKeyPreviousTrack = 0xB1;
    private const int MediaKeyStop = 0xB2;
    private const int VolumeKeyMute = 0xAD;
    private const int VolumeKeyDown = 0xAE;
    private const int VolumeKeyUp = 0xAF;
    private const int VkCapsLock = 0x14;
    private const int VkNumLock = 0x90;
    private const int VkScrollLock = 0x91;
    private const int VkInsert = 0x2D;
    private const int LockKeyThrottleMs = 120;

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
        using (Process curProcess = Process.GetCurrentProcess())
        using (ProcessModule? curModule = curProcess.MainModule)
        {
            if (curModule == null)
            {
                return IntPtr.Zero;
            }

            return SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(curModule.ModuleName), 0);
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        bool isKeyDown = wParam == (IntPtr)WM_KEYDOWN;
        bool isKeyUp = wParam == (IntPtr)WM_KEYUP;
        if (nCode < 0 || (!isKeyDown && !isKeyUp))
        {
            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        int vkCode = Marshal.ReadInt32(lParam);

        bool mediaKeyPressed = vkCode == MediaKeyPlayPause || vkCode == MediaKeyNextTrack || vkCode == MediaKeyPreviousTrack || vkCode == MediaKeyStop;
        bool volumeKeyPressed = vkCode == VolumeKeyMute || vkCode == VolumeKeyDown || vkCode == VolumeKeyUp;

        if (mediaKeyPressed || (!SettingsManager.Current.MediaFlyoutVolumeKeysExcluded && volumeKeyPressed))
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
                else
                {
                    DispatchEventAsync(DispatchMediaKeyPressed);
                }
            }
        }

        // Emits lock key events separately so UI policies can be applied by subscribers.
        switch (vkCode)
        {
            case VkCapsLock:
                RaiseLockKeyPressed(LockKeyType.CapsLock, Keyboard.IsKeyToggled(Key.CapsLock));
                break;
            case VkNumLock:
                RaiseLockKeyPressed(LockKeyType.NumLock, Keyboard.IsKeyToggled(Key.NumLock));
                break;
            case VkScrollLock:
                RaiseLockKeyPressed(LockKeyType.ScrollLock, Keyboard.IsKeyToggled(Key.Scroll));
                break;
            case VkInsert:
                RaiseLockKeyPressed(LockKeyType.Insert, Keyboard.IsKeyToggled(Key.Insert));
                break;
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private void RaiseLockKeyPressed(LockKeyType keyType, bool isToggled)
    {
        long currentTime = Environment.TickCount64;
        long lastLockKeyEventTime = Interlocked.Read(ref _lastLockKeyEventTime);
        if ((currentTime - lastLockKeyEventTime) < LockKeyThrottleMs)
        {
            return;
        }

        Interlocked.Exchange(ref _lastLockKeyEventTime, currentTime);
        DispatchEventAsync(() => LockKeyPressed?.Invoke(this, new LockKeyPressedEventArgs(InputTrigger.KeyboardHook, keyType, isToggled)));
    }

    private void DispatchEventAsync(Action action)
    {
        _dispatchQueue.Enqueue(action);
        _dispatchSignal.Release();
    }

    private void DispatchVolumeChanged()
    {
        Logger.Debug("Volume key detected via keyboard hook");
        VolumeChanged?.Invoke(this, new VolumeChangedEventArgs(InputTrigger.KeyboardHook));
    }

    private void DispatchMediaKeyPressed()
    {
        Logger.Debug("Media key detected via keyboard hook");
        MediaKeyPressed?.Invoke(this, new MediaKeyPressedEventArgs(InputTrigger.KeyboardHook));
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
