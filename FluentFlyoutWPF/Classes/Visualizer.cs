// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes.Settings;
using FluentFlyout.Classes.Utils;
using Microsoft.Win32;
using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FluentFlyoutWPF.Classes
{
    public class Visualizer : IDisposable
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        public static int BarCount = 10;

        // Keep at 2: only an exact 2:1 minification turns WPF's default Linear
        // filter into a true box average. Other factors discard the supersampling.
        private const int Supersample = 2;

        // user-configurable width of the bar area (DIP), see TaskbarVisualizerWidth
        public const int MinImageWidth = 40;
        public const int MaxImageWidth = 140;
        public const int DefaultImageWidth = 76;

        // visualizer styles, see TaskbarVisualizerStyle
        public const int StyleRounded = 0;
        public const int StyleSquare = 1;
        public const int StyleThin = 2;
        public const int StyleGradient = 3;
        public const int StyleSegmented = 4;
        public const int StyleWaveform = 5;
        public const int StyleCount = 6;

        // visualizer color modes, see TaskbarVisualizerColorMode
        public const int ColorModeSingle = 0;
        public const int ColorModeAlbumArt = 1;
        public const int ColorModeRainbow = 2;
        public const int ColorModeCount = 3;

        private int ImageWidth => Math.Clamp(SettingsManager.Current.TaskbarVisualizerWidth, MinImageWidth, MaxImageWidth) * Supersample;
        private readonly int ImageHeight = 32 * Supersample;
        private readonly int BarSpacing = 2 * Supersample;

        /// <summary>
        /// Raised when the backing bitmap is (re)created (e.g. after the width setting changed),
        /// so the hosting control can rebind its image source.
        /// </summary>
        public event Action<WriteableBitmap?>? BitmapChanged;

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
        private string? _deviceId; // track current device ID for restart logic

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

        /// <summary>
        /// Recreates the bitmap when the configured width changed and redraws it.
        /// Used when the width or the style setting is changed from the settings UI.
        /// </summary>
        public void RefreshBitmap()
        {
            int targetWidth = ImageWidth;

            bool needsRecreate;
            lock (_lock)
            {
                needsRecreate = _bitmap == null || _bitmap.PixelWidth != targetWidth || _bitmap.PixelHeight != ImageHeight;
            }

            if (needsRecreate)
            {
                InitializeBitmap();
                BitmapChanged?.Invoke(Bitmap);
            }

            UpdateBitmap();
        }

        private void OnDefaultDeviceChanged(object? sender, DefaultDeviceChangedEventArgs e)
        {
            _deviceId = e.DeviceId;

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
                _renderDevice = string.IsNullOrWhiteSpace(_deviceId)
                     ? AudioDeviceMonitor.Instance.GetDefaultRenderDevice()
                     : AudioDeviceMonitor.Instance.GetDeviceById(_deviceId) ?? AudioDeviceMonitor.Instance.GetDefaultRenderDevice();

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

                        if (!SettingsManager.Current.TaskbarVisualizerBaseline || SettingsManager.Current.TaskbarVisualizerBaselineAutoHide) // if baseline is enabled and autohide is off, condition is false
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

                if (SettingsManager.Current.TaskbarVisualizerBaseline && !SettingsManager.Current.TaskbarVisualizerBaselineAutoHide)
                {
                    // if baseline is enabled and autohide is off, we want to keep showing the bars even when they are all zero
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
            // Resolve the palette once per frame (see TaskbarVisualizerColorMode)
            (byte r, byte g, byte b)[] barColors = GetBarColors();

            bool centeredBars = SettingsManager.Current.TaskbarVisualizerCenteredBars;
            int barBaseline = SettingsManager.Current.TaskbarVisualizerBaseline ? Supersample * 2 : 0;

            int centerY = ImageHeight / 2;

            int style = Math.Clamp(SettingsManager.Current.TaskbarVisualizerStyle, 0, StyleCount - 1);

            // Horizontal layout 
            ComputeLayout(ImageWidth, BarCount, BarSpacing,
                out int slotWidth,
                out int offsetX);

            // AA constants 
            const float aa = 1.25f;
            float invAA = 1f / aa;

            if (style == StyleWaveform)
            {
                DrawWaveform(buffer, stride, offsetX, slotWidth, centerY, barBaseline, centeredBars, barColors);
                return;
            }

            // "thin" draws a narrow bar centered inside its slot
            int barWidth = style == StyleThin ? Math.Max(Supersample, slotWidth / 3) : slotWidth;

            // Radius 
            float baseRadius = GetCornerRadius();

            for (int i = 0; i < BarCount; i++)
            {
                int slotX = offsetX + i * (slotWidth + BarSpacing);
                int barX = slotX + (slotWidth - barWidth) / 2;

                int barHeight = GetBarHeight(_barValues[i], barBaseline);

                if (barHeight <= 0)
                    continue;

                ComputeVertical(centeredBars, centerY, barHeight, out int barY, out int barEndY);

                // Clamp radius per bar
                float radius = style switch
                {
                    StyleSquare => 0f,
                    StyleThin => barWidth * 0.5f,
                    _ => ClampRadius(baseRadius, barWidth, barHeight),
                };

                var (r, g, b) = i < barColors.Length ? barColors[i] : barColors[^1];

                RasterizeBar(
                    buffer, stride,
                    barX, barWidth,
                    barY, barEndY,
                    centeredBars,
                    radius, radius * radius, invAA,
                    b, g, r,
                    style == StyleSegmented ? Supersample : 0,
                    style == StyleGradient);
            }
        }

        /// <summary>
        /// Colors used by the visualizer, one entry per bar, based on <c>TaskbarVisualizerColorMode</c>:
        /// 0 = single color (accent / cover's dominant color), 1 = gradient built from the album art palette,
        /// 2 = rainbow sweep.
        /// </summary>
        private (byte r, byte g, byte b)[] GetBarColors()
        {
            int count = Math.Min(BarCount, _barValues?.Length ?? 0);
            if (count <= 0)
                return [];

            var colors = new (byte r, byte g, byte b)[count];
            int mode = Math.Clamp(SettingsManager.Current.TaskbarVisualizerColorMode, 0, ColorModeCount - 1);

            if (mode == ColorModeAlbumArt)
            {
                var palette = BitmapHelper.GetVisualizerPalette(4);
                if (palette.Count >= 2)
                {
                    for (int i = 0; i < count; i++)
                    {
                        float t = count == 1 ? 0f : i / (float)(count - 1);
                        colors[i] = SamplePalette(palette, t);
                    }
                    return colors;
                }

                // only one color available -> use it for every bar
                if (palette.Count == 1)
                {
                    Color single = palette[0].Color;
                    for (int i = 0; i < count; i++)
                        colors[i] = (single.R, single.G, single.B);
                    return colors;
                }
            }
            else if (mode == ColorModeRainbow)
            {
                // start the sweep at the cover's dominant hue so it still matches the artwork
                double baseHue = 0;
                var coverColors = BitmapHelper.GetVisualizerPalette(1);
                if (coverColors.Count > 0)
                    baseHue = GetHue(coverColors[0].Color);

                for (int i = 0; i < count; i++)
                {
                    float t = count == 1 ? 0f : i / (float)(count - 1);
                    colors[i] = FromHsv((baseHue + (t * 300.0)) % 360.0, 0.78, 0.95);
                }
                return colors;
            }

            // single color for all bars
            SolidColorBrush brush = BitmapHelper.SavedDominantColors.Count > 0
                ? BitmapHelper.SavedDominantColors.Last()
                : (SolidColorBrush)Application.Current.TryFindResource("MicaWPF.Brushes.SystemAccentColorTertiary");

            Color flat = brush.Color;
            for (int i = 0; i < count; i++)
                colors[i] = (flat.R, flat.G, flat.B);

            return colors;
        }

        private static (byte r, byte g, byte b) SamplePalette(List<SolidColorBrush> palette, float t)
        {
            t = Math.Clamp(t, 0f, 1f);

            float scaled = t * (palette.Count - 1);
            int i0 = (int)MathF.Floor(scaled);
            int i1 = Math.Min(i0 + 1, palette.Count - 1);
            float f = scaled - i0;

            Color c0 = palette[i0].Color;
            Color c1 = palette[i1].Color;

            return ((byte)(c0.R + (c1.R - c0.R) * f),
                    (byte)(c0.G + (c1.G - c0.G) * f),
                    (byte)(c0.B + (c1.B - c0.B) * f));
        }

        private static (byte r, byte g, byte b) SampleRamp((byte r, byte g, byte b)[] ramp, float t)
        {
            if (ramp.Length == 0)
                return (255, 255, 255);
            if (ramp.Length == 1)
                return ramp[0];

            t = Math.Clamp(t, 0f, 1f);

            float scaled = t * (ramp.Length - 1);
            int i0 = (int)MathF.Floor(scaled);
            int i1 = Math.Min(i0 + 1, ramp.Length - 1);
            float f = scaled - i0;

            return ((byte)(ramp[i0].r + (ramp[i1].r - ramp[i0].r) * f),
                    (byte)(ramp[i0].g + (ramp[i1].g - ramp[i0].g) * f),
                    (byte)(ramp[i0].b + (ramp[i1].b - ramp[i0].b) * f));
        }

        private static double GetHue(Color c)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double delta = max - min;

            if (delta < 1e-6)
                return 0;

            double hue;
            if (max == r) hue = 60 * (((g - b) / delta) % 6);
            else if (max == g) hue = 60 * (((b - r) / delta) + 2);
            else hue = 60 * (((r - g) / delta) + 4);

            return (hue + 360) % 360;
        }

        private static (byte r, byte g, byte b) FromHsv(double h, double s, double v)
        {
            double c = v * s;
            double x = c * (1 - Math.Abs(((h / 60.0) % 2) - 1));
            double m = v - c;

            double r, g, b;
            if (h < 60) { r = c; g = x; b = 0; }
            else if (h < 120) { r = x; g = c; b = 0; }
            else if (h < 180) { r = 0; g = c; b = x; }
            else if (h < 240) { r = 0; g = x; b = c; }
            else if (h < 300) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }

            return ((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
        }

        /// <summary>
        /// Style 5 (waveform): a continuous line that follows the interpolated bar values.
        /// </summary>
        private unsafe void DrawWaveform(
            Span<byte> buffer,
            int stride,
            int offsetX,
            int slotWidth,
            int centerY,
            int barBaseline,
            bool centeredBars,
            (byte r, byte g, byte b)[] barColors)
        {
            if (_barValues == null || _barValues.Length == 0)
                return;

            int count = Math.Min(BarCount, _barValues.Length);
            if (count <= 0)
                return;

            int thickness = Supersample + 1;
            float pitch = slotWidth + BarSpacing;

            for (int x = 0; x < ImageWidth; x++)
            {
                float pos = (x - offsetX) / pitch - 0.5f;
                float value;

                if (pos <= 0f)
                {
                    value = _barValues[0];
                }
                else if (pos >= count - 1)
                {
                    value = _barValues[count - 1];
                }
                else
                {
                    int i0 = (int)pos;
                    float f = pos - i0;
                    float v0 = _barValues[i0];
                    float v1 = i0 + 1 < count ? _barValues[i0 + 1] : v0;
                    value = v0 + (v1 - v0) * f;
                }

                int height = GetBarHeight(value, barBaseline);
                if (height <= 0)
                    continue;

                int lineY = centeredBars ? centerY - (height >> 1) : ImageHeight - height;

                int yStart = Math.Max(0, lineY - thickness / 2);
                int yEnd = Math.Min(ImageHeight, yStart + thickness);

                // follow the palette along the horizontal axis
                float colorT = ImageWidth <= 1 ? 0f : x / (float)(ImageWidth - 1);
                var (cr, cg, cb) = SampleRamp(barColors, colorT);

                for (int y = yStart; y < yEnd; y++)
                {
                    int index = y * stride + (x << 2);
                    if (index + 3 >= buffer.Length)
                        continue;

                    WritePixel(buffer, index, cb, cg, cr, 255);
                }
            }
        }

        private static void ComputeLayout(
            int imageWidth,
            int barCount,
            int spacing,
            out int barWidth,
            out int offsetX)
        {
            int totalSpacing = (barCount - 1) * spacing;

            int availableWidth = imageWidth - totalSpacing - 1;

            barWidth = availableWidth / barCount;

            int usedWidth = barWidth * barCount + totalSpacing;

            // Center safely
            offsetX = (imageWidth - usedWidth) >> 1;
        }

        private void ComputeVertical(bool centered, int centerY, int height, out int y, out int endY)
        {
            if (centered)
            {
                int half = height >> 1; // faster than /2
                y = centerY - half;
                endY = centerY + half;
            }
            else
            {
                y = ImageHeight - height;
                endY = ImageHeight;
            }
        }

        private int GetBarHeight(float value, int baseline)
        {
            return Math.Max((int)(Math.Clamp(value, 0f, 1f) * ImageHeight), baseline);
        }
        private static float GetCornerRadius()
        {
            return (2f * Supersample) / MathF.Max(1f, SettingsManager.Current.TaskbarVisualizerBarCount / 10f);
        }

        private static float ClampRadius(float r, int width, int height)
        {
            float max = MathF.Min(width, height) * 0.5f;
            return r > max ? max : r;
        }

        private unsafe void RasterizeBar(
            Span<byte> buffer,
            int stride,
            int barX,
            int barWidth,
            int barY,
            int barEndY,
            bool centeredBars,
            float radius,
            float radiusSq,
            float invAA,
            byte b, byte g, byte r,
            int segmentGap = 0,
            bool verticalFade = false)
        {
            float left = barX;
            float right = barX + barWidth;
            float top = barY;
            float bottom = barEndY;

            float innerLeft = left + radius;
            float innerRight = right - radius;
            float innerTop = top + radius;
            float innerBottom = bottom - radius;

            int barHeight = barEndY - barY;
            int segmentPitch = segmentGap * 3;

            for (int y = barY; y < barEndY && y < ImageHeight && y >= 0; y++)
            {
                // segmented (VU meter) styles leave small gaps between the segments
                if (segmentGap > 0 && ((barEndY - 1 - y) % segmentPitch) < segmentGap)
                    continue;

                // gradient styles fade the bar out towards its base
                int rowAlpha = 255;
                if (verticalFade && barHeight > 1)
                {
                    float t = (y - barY) / (float)(barHeight - 1); // 0 at the tip, 1 at the base
                    rowAlpha = (int)(255 * (1f - 0.7f * t));
                }

                int row = y * stride;
                float py = y + 0.5f;

                for (int x = barX; x < barX + barWidth && x < ImageWidth; x++)
                {
                    int index = row + (x << 2); // x * 4 (bitshift faster)
                    if (index + 3 >= buffer.Length)
                        continue;

                    float px = x + 0.5f;

                    // CENTER
                    if (px >= innerLeft && px <= innerRight)
                    {
                        WritePixel(buffer, index, b, g, r, (byte)rowAlpha);
                        continue;
                    }

                    // SIDES
                    if (py >= innerTop && py <= innerBottom)
                    {
                        WritePixel(buffer, index, b, g, r, (byte)rowAlpha);
                        continue;
                    }

                    // FLAT BOTTOM
                    if (!centeredBars && py >= innerBottom)
                    {
                        WritePixel(buffer, index, b, g, r, (byte)rowAlpha);
                        continue;
                    }

                    // CORNERS
                    float cx = px < innerLeft ? innerLeft : (px > innerRight ? innerRight : px);
                    float cy = py < innerTop ? innerTop : (py > innerBottom ? innerBottom : py);

                    float dx = px - cx;
                    float dy = py - cy;

                    float distSq = dx * dx + dy * dy;
                    float sdf = (distSq - radiusSq) / (2f * radius);

                    float alpha = 0.5f - sdf * invAA;

                    if (alpha <= 0f)
                        continue;

                    if (alpha > 1f) alpha = 1f;

                    WritePixel(buffer, index, b, g, r, (byte)(rowAlpha * alpha));
                }
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