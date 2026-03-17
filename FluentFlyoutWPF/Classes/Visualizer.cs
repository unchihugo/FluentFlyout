// Copyright © 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes.Settings;
using FluentFlyout.Classes.Utils;
using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;
using Microsoft.Win32;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FluentFlyoutWPF.Classes
{
    public class Visualizer : IDisposable
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        public static int BarCount = 10;
        private readonly int ImageWidth = 76 * 3;
        private readonly int ImageHeight = 32 * 3;
        private readonly int BarSpacing = 2 * 3;

        private WasapiLoopbackCapture? _capture;
        private MMDevice? _renderDevice;
        private static float[]? _barValues;
        private WriteableBitmap? _bitmap;
        private bool _isRunning;
        private readonly object _lock = new();

        private readonly int _fftLength = 4096;
        private int _fftPos = 0;
        private readonly Complex[] _fftBuffer;

        private readonly int _targetFps = 30;
        private DateTime _lastUpdateTime = DateTime.MinValue;

        private System.Timers.Timer? _captureWatchdog;
        private DateTime _lastDataAvailableUtc = DateTime.MinValue;
        private int _restartInProgress; // 0=false, 1=true (Interlocked)

        private readonly struct BarGeometry
        {
            public readonly float Left, Right, Top, Bottom;
            public readonly float InnerLeft, InnerRight, InnerTop, InnerBottom;

            public BarGeometry(int x, int width, int y, int endY, float radius)
            {
                Left = x;
                Right = x + width;
                Top = y;
                Bottom = endY;

                InnerLeft = Left + radius;
                InnerRight = Right - radius;
                InnerTop = Top + radius;
                InnerBottom = Bottom - radius;
            }
        }

        public WriteableBitmap? Bitmap
        {
            get
            {
                lock (_lock)
                {
                    return _bitmap;
                }
            }
        }

        public Visualizer()
        {
            InitializeBitmap();

            _fftBuffer = new Complex[_fftLength];

            ResizeBarList(SettingsManager.Current.TaskbarVisualizerBarCount);
            AudioDeviceMonitor.Instance.DefaultDeviceChanged += OnDefaultDeviceChanged;
            TryRegisterSystemEvents();
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
                // On some environments (e.g. non-interactive sessions), SystemEvents may not be available.
                Logger.Warn(ex, "Failed to register SystemEvents handlers for visualizer auto-restart");
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
                Logger.Warn(ex, "Failed to unregister SystemEvents handlers for visualizer auto-restart");
            }
        }

        private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (!SettingsManager.Current.TaskbarVisualizerEnabled)
                return;

            // When unlocking after device disconnect (e.g. Bluetooth earbuds), WASAPI loopback can get stuck.
            // Restart capture on unlock / logon to recover without user action.
            if (e.Reason == SessionSwitchReason.SessionUnlock || e.Reason == SessionSwitchReason.SessionLogon)
            {
                RequestRestart($"session switch: {e.Reason}");
            }
        }

        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (!SettingsManager.Current.TaskbarVisualizerEnabled)
                return;

            if (e.Mode == PowerModes.Resume)
            {
                RequestRestart("power resume");
            }
        }

        private void InitializeBitmap()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                lock (_lock)
                {
                    _bitmap = new WriteableBitmap(ImageWidth, ImageHeight, 96, 96, PixelFormats.Bgra32, null);
                }
            });
        }

        private void OnDefaultDeviceChanged(object? sender, DefaultDeviceChangedEventArgs e)
        {
            if (e.DataFlow != DataFlow.Render || e.Role != Role.Multimedia)
                return;

            // Even if capture isn't currently running (e.g. restart attempt failed while the device was reconfiguring),
            // we still want to try restarting as soon as Windows reports a usable default endpoint again.
            if (!SettingsManager.Current.TaskbarVisualizerEnabled)
                return;
            RequestRestart("default audio output device changed");
        }

        private void RequestRestart(string reason)
        {
            if (!SettingsManager.Current.TaskbarVisualizerEnabled)
                return;

            if (Interlocked.Exchange(ref _restartInProgress, 1) == 1)
                return;

            Logger.Info($"Restarting visualizer ({reason})");

            Task.Run(async () =>
            {
                try
                {
                    Stop();

                    for (int attempt = 0; attempt < 5; attempt++)
                    {
                        await Task.Delay(500);
                        Start();
                        if (_isRunning)
                            return;
                        Logger.Warn($"Visualizer restart attempt {attempt + 1} failed, retrying...");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Visualizer restart failed");
                }
                finally
                {
                    Interlocked.Exchange(ref _restartInProgress, 0);
                }
            });
        }

        public static void ResizeBarList(int newBarCount)
        {
            BarCount = newBarCount;
            _barValues = new float[BarCount];
        }

        public void Start()
        {
            if (_isRunning)
                return;

            float barCount = BarCount >= 0 ? BarCount : 8;
            _barValues = new float[(int)barCount];

            try
            {
                // Explicitly bind to the current default render endpoint.
                // Using the parameterless capture can throw transient COM errors when the default endpoint is
                // reconfiguring (e.g. Bluetooth earbuds disconnect/reconnect around lock/unlock).
                _renderDevice?.Dispose();
                _renderDevice = AudioDeviceMonitor.Instance.GetDefaultRenderDevice();

                if (_renderDevice == null)
                {
                    return;
                }

                _capture = new WasapiLoopbackCapture(_renderDevice);
                _capture.DataAvailable += OnDataAvailable;
                _capture.RecordingStopped += OnRecordingStopped;
                _capture.StartRecording();
                _isRunning = true;
                _lastDataAvailableUtc = DateTime.UtcNow;

                // automatic update timer in case audio data is not updated
                _captureWatchdog = new(500)
                {
                    AutoReset = false
                };
                _captureWatchdog.Elapsed += (_, _) =>
                {
                    if (_isRunning)
                    {
                        for (int i = 0; i < _barValues.Length; i++)
                        {
                            _barValues[i] = 0;
                        }
                        UpdateBitmap();

                        if (!SettingsManager.Current.TaskbarVisualizerBaseline) // if baseline is enabled, don't switch the setting
                            SettingsManager.Current.TaskbarVisualizerHasContent = false;

                        // If we stop receiving loopback callbacks entirely (common after lock/unlock + device changes),
                        // the timer fires once and then never again. Use it as a recovery trigger.
                        var silenceFor = DateTime.UtcNow - _lastDataAvailableUtc;
                        if (silenceFor > TimeSpan.FromSeconds(2))
                        {
                            RequestRestart($"no audio callbacks for {silenceFor.TotalSeconds:0.0}s");
                        }
                    }
                };
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to start visualizer");
            }
        }

        public void Stop()
        {
            if (!_isRunning)
                return;

            _isRunning = false;

            _capture?.DataAvailable -= OnDataAvailable;
            _capture?.RecordingStopped -= OnRecordingStopped;
            _capture?.StopRecording();
            _capture?.Dispose();
            _capture = null;

            _renderDevice?.Dispose();
            _renderDevice = null;

            _captureWatchdog?.Stop();
            _captureWatchdog?.Dispose();
            _captureWatchdog = null;
        }

        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (!_isRunning || e.BytesRecorded == 0)
                return;

            _lastDataAvailableUtc = DateTime.UtcNow;

            _captureWatchdog.Stop();
            _captureWatchdog.Start();

            int bytesPerSample = _capture!.WaveFormat.BitsPerSample / 8;
            int samplesRecorded = e.BytesRecorded / bytesPerSample;

            for (int i = 0; i < samplesRecorded; i++)
            {
                float sampleValue = 0;
                if (bytesPerSample == 4)
                {
                    sampleValue = BitConverter.ToSingle(e.Buffer, i * 4);
                }
                else if (bytesPerSample == 2)
                {
                    sampleValue = BitConverter.ToInt16(e.Buffer, i * 2) / 32768f;
                }

                _fftBuffer[_fftPos].X = (float)(sampleValue * FastFourierTransform.HammingWindow(_fftPos, _fftLength));
                _fftBuffer[_fftPos].Y = 0;
                _fftPos++;

                // When buffer isn't full, skip processing and continue filling
                if (_fftPos < _fftLength)
                    continue;

                // perform FFT
                _fftPos = 0;
                ProcessFftData();

                // Update UI with frame rate limiting
                DateTime now = DateTime.UtcNow;
                double minFrameTime = 1000.0 / _targetFps;
                double timeSinceLastUpdate = (now - _lastUpdateTime).TotalMilliseconds;

                if (timeSinceLastUpdate < minFrameTime)
                    continue;

                _lastUpdateTime = now;
                SettingsManager.Current.TaskbarVisualizerHasContent = true;

                if (SettingsManager.Current.TaskbarVisualizerBaseline)
                {
                    // if baseline is enabled, we want to keep showing the bars even when they are all zero
                    UpdateBitmap();
                    break;
                }

                // check if bars are all zero, if so set has content to false to disable hover effect
                bool allZero = true;
                for (int j = 0; j < BarCount; j++)
                {
                    if (_barValues[j] > 0.01f)
                    {
                        allZero = false;
                        break;
                    }
                }

                // update bars if they have content
                if (!allZero)
                    UpdateBitmap();
                else
                    SettingsManager.Current.TaskbarVisualizerHasContent = false;
            }
        }

        private void ProcessFftData()
        {
            FastFourierTransform.FFT(true, (int)Math.Log(_fftLength, 2.0), _fftBuffer);

            int sampleRate = _capture.WaveFormat.SampleRate;
            double frequencyPerBin = (double)sampleRate / _fftLength;

            double minFreq = 40;   // Hz
            double maxFreq = 8000; // Hz
            //double minFreq = 40;  // Hz // could be a setting to be bass only
            //double maxFreq = 120; // Hz
            float minDb = (SettingsManager.Current.TaskbarVisualizerAudioSensitivity * -10f) - 30f;
            float maxDb = (SettingsManager.Current.TaskbarVisualizerAudioPeakLevel * 10f) - 30f;

            float[] currentBars = new float[BarCount];

            for (int i = 0; i < BarCount; i++)
            {
                double startFreq = minFreq * Math.Pow(maxFreq / minFreq, (double)i / BarCount);
                double endFreq = minFreq * Math.Pow(maxFreq / minFreq, (double)(i + 1) / BarCount);

                int startBin = (int)(startFreq / frequencyPerBin);
                int endBin = (int)(endFreq / frequencyPerBin);

                if (endBin <= startBin) endBin = startBin + 1;
                if (endBin >= _fftBuffer.Length / 2) endBin = _fftBuffer.Length / 2 - 1;

                float maxAmplitude = 0;

                // Find max amplitude
                for (int j = startBin; j < endBin; j++)
                {
                    float amplitude = (float)Math.Sqrt(_fftBuffer[j].X * _fftBuffer[j].X + _fftBuffer[j].Y * _fftBuffer[j].Y);
                    if (amplitude > maxAmplitude)
                        maxAmplitude = amplitude;
                }

                float progress = (float)i / BarCount;
                float linearBoost = 1.0f + (progress * 75.0f);
                maxAmplitude *= linearBoost;

                if (maxAmplitude < 0.001f) maxAmplitude = 0.001f;

                float db = 20f * (float)Math.Log10(maxAmplitude);

                float intensity = (db - minDb) / (maxDb - minDb);
                intensity = Math.Clamp(intensity, 0f, 1f);

                currentBars[i] = intensity;
            }

            for (int i = 0; i < BarCount; i++)
            {
                if (currentBars[i] > _barValues[i])
                {
                    // Jump up quickly
                    _barValues[i] = currentBars[i];
                }
                else
                {
                    // Fall down slowly
                    //_barValues[i] = (_barValues[i] * 0.9f) + (currentBars[i] * 0.1f);
                    _barValues[i] = (_barValues[i] * 0.8f) + (currentBars[i] * 0.2f);
                    //_barValues[i] = (_barValues[i] * 0.7f) + (currentBars[i] * 0.3f); // could be options for smoothening
                    //_barValues[i] = (_barValues[i] * 0.6f) + (currentBars[i] * 0.4f);
                }
            }
        }

        private void UpdateBitmap()
        {
            if (_bitmap == null)
                return;

            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                lock (_lock)
                {
                    if (_bitmap == null)
                        return;

                    _bitmap.Lock();

                    try
                    {
                        unsafe
                        {
                            IntPtr pBackBuffer = _bitmap.BackBuffer;
                            int stride = _bitmap.BackBufferStride;
                            int bufferSize = stride * ImageHeight;

                            Span<byte> buffer = new Span<byte>(pBackBuffer.ToPointer(), bufferSize);

                            buffer.Clear();

                            DrawBars(stride, buffer);
                        }

                        _bitmap.AddDirtyRect(new Int32Rect(0, 0, ImageWidth, ImageHeight));
                    }
                    finally
                    {
                        _bitmap.Unlock();
                    }
                }
            }, System.Windows.Threading.DispatcherPriority.Render);
        }

        private unsafe void DrawBars(int stride, Span<byte> buffer)
        {
            // Resolve brush once (avoid repeated lookups)
            SolidColorBrush brush = BitmapHelper.SavedDominantColors.Count > 0
                ? BitmapHelper.SavedDominantColors.Last()
                : (SolidColorBrush)Application.Current.TryFindResource("MicaWPF.Brushes.SystemAccentColorTertiary");

            byte b = brush.Color.B;
            byte g = brush.Color.G;
            byte r = brush.Color.R;

            bool centeredBars = SettingsManager.Current.TaskbarVisualizerCenteredBars;
            int barBaseline = SettingsManager.Current.TaskbarVisualizerBaseline ? 4 : 0;

            int centerY = ImageHeight / 2;

            // Layout calculation (float-based to avoid spacing drift) 
            float totalSpacing = (BarCount - 1) * BarSpacing;
            float exactBarWidth = (ImageWidth - totalSpacing) / (float)BarCount;
            float totalBarsWidth = BarCount * exactBarWidth + totalSpacing;
            float offsetX = (ImageWidth - totalBarsWidth) * 0.5f;

            for (int i = 0; i < BarCount; i++)
            {
                // Normalize and scale height
                float normalizedValue = Math.Clamp(_barValues[i], 0f, 1f);
                int barHeight = Math.Max((int)(normalizedValue * ImageHeight), barBaseline);

                // Compute precise X bounds (prevents cumulative rounding errors)
                ComputeBarBounds(i, exactBarWidth, offsetX, out int barX, out int barWidth);

                if (barWidth <= 0)
                    continue;

                // Compute vertical bounds
                ComputeBarVertical(centeredBars, centerY, barHeight, out int barY, out int barEndY);

                // Precompute geometry
                float radius = MathF.Min(barWidth, barHeight) * 0.5f;

                var geom = new BarGeometry(barX, barWidth, barY, barEndY, radius);

                // Precompute SDF constants
                float aa = 1.25f;
                float invAA = 1f / aa;
                float radiusSq = radius * radius;

                // Rasterize
                RasterizeBar(buffer, stride, geom, centeredBars, b, g, r, invAA, radiusSq);
            }
        }

        private unsafe void RasterizeBar(
            Span<byte> buffer,
            int stride,
            BarGeometry g,
            bool centeredBars,
            byte b, byte gr, byte r,
            float invAA,
            float radiusSq)
        {
            for (int y = (int)g.Top; y < g.Bottom && y < ImageHeight && y >= 0; y++)
            {
                int row = y * stride;

                for (int x = (int)g.Left; x < g.Right && x < ImageWidth; x++)
                {
                    int index = row + x * 4;
                    if (index + 3 >= buffer.Length)
                        continue;

                    // Fast path: center rectangle 
                    if (x >= g.InnerLeft && x <= g.InnerRight)
                    {
                        WritePixel(buffer, index, b, gr, r, 255);
                        continue;
                    }

                    // Fast path: vertical sides (no AA) 
                    if (y >= g.InnerTop && y <= g.InnerBottom)
                    {
                        WritePixel(buffer, index, b, gr, r, 255);
                        continue;
                    }

                    // Flat bottom if not centered 
                    if (!centeredBars && y >= g.InnerBottom)
                    {
                        WritePixel(buffer, index, b, gr, r, 255);
                        continue;
                    }

                    // Rounded caps only 
                    float cx = x < g.InnerLeft ? g.InnerLeft : (x > g.InnerRight ? g.InnerRight : x);
                    float cy = y < g.InnerTop ? g.InnerTop : (y > g.InnerBottom ? g.InnerBottom : y);

                    float dx = x - cx;
                    float dy = y - cy;

                    float distSq = dx * dx + dy * dy;
                    float sdf = (distSq - radiusSq) / (2f * MathF.Sqrt(radiusSq)); // radius = sqrt(radiusSq)

                    float alpha = Math.Clamp(0.5f - sdf * invAA, 0f, 1f);

                    if (alpha <= 0f)
                        continue;

                    WritePixel(buffer, index, b, gr, r, (byte)(255 * alpha));
                }
            }
        }

        private void ComputeBarBounds(int i, float width, float offsetX, out int x, out int barWidth)
        {
            float fx = offsetX + i * (width + BarSpacing);
            float fxNext = fx + width;

            x = (int)MathF.Round(fx);
            int right = (int)MathF.Round(fxNext);
            barWidth = right - x;
        }

        private void ComputeBarVertical(bool centered, int centerY, int height, out int y, out int endY)
        {
            if (centered)
            {
                int half = height / 2;
                y = centerY - half;
                endY = centerY + half;
            }
            else
            {
                y = ImageHeight - height;
                endY = ImageHeight;
            }
        }

        private static void WritePixel(Span<byte> buffer, int index, byte b, byte g, byte r, byte a)
        {
            buffer[index] = b;
            buffer[index + 1] = g;
            buffer[index + 2] = r;
            buffer[index + 3] = a;
        }

        private void OnRecordingStopped(object? sender, StoppedEventArgs e)
        {
            if (e.Exception != null)
            {
                Logger.Error(e.Exception, "Visualizer recording stopped due to an error");
            }
        }

        public void Dispose()
        {
            Stop();

            AudioDeviceMonitor.Instance.DefaultDeviceChanged -= OnDefaultDeviceChanged;
            TryUnregisterSystemEvents();

            if (_capture != null)
            {
                _capture.DataAvailable -= OnDataAvailable;
                _capture.RecordingStopped -= OnRecordingStopped;
                _capture.Dispose();
                _capture = null;
            }

            GC.SuppressFinalize(this);
        }
    }
}