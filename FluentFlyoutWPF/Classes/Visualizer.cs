using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentFlyout.Classes.Settings;
using NAudio.Dsp;
using NAudio.Wave;

namespace FluentFlyoutWPF.Classes
{
    public class Visualizer : IDisposable
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        public static int BarCount = 10;
        public int ImageWidth = 76*3;
        public int ImageHeight = 32*3;
        public int BarSpacing = 2*3;

        private WasapiLoopbackCapture? _capture;
        private static float[]? _barValues;
        private WriteableBitmap? _bitmap;
        private bool _isRunning;
        private readonly object _lock = new();

        private readonly int _fftLength = 4096;
        private int _fftPos = 0;
        private Complex[] _fftBuffer;

        private int _targetFps = 30;
        private DateTime _lastUpdateTime = DateTime.MinValue;

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

        public int TargetFps
        {
            get => _targetFps;
            set => _targetFps = Math.Clamp(value, 1, 120);
        }

        public event EventHandler? BitmapUpdated;

        public Visualizer()
        {
            InitializeBitmap();

            _fftBuffer = new Complex[_fftLength];
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
                _capture = new WasapiLoopbackCapture();
                _capture.DataAvailable += OnDataAvailable;
                _capture.RecordingStopped += OnRecordingStopped;
                _capture.StartRecording();
                _isRunning = true;
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
        }

        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (!_isRunning || e.BytesRecorded == 0)
                return;

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

                // When buffer is full, perform FFT
                if (_fftPos >= _fftLength)
                {
                    _fftPos = 0;
                    ProcessFftData();
                    
                    // Update UI with frame rate limiting
                    DateTime now = DateTime.UtcNow;
                    double minFrameTime = 1000.0 / _targetFps;
                    double timeSinceLastUpdate = (now - _lastUpdateTime).TotalMilliseconds;
                    
                    if (timeSinceLastUpdate >= minFrameTime)
                    {
                        _lastUpdateTime = now;
                        UpdateBitmap();
                    }
                }
            }
        }

        private void ProcessFftData()
        {
            FastFourierTransform.FFT(true, (int)Math.Log(_fftLength, 2.0), _fftBuffer);

            int sampleRate = _capture.WaveFormat.SampleRate;
            double frequencyPerBin = (double)sampleRate / _fftLength;

            double minFreq = 40;   // Hz
            double maxFreq = 8000; // Hz

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

                float minDb = -50f;
                float maxDb = -10f;

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
                    _barValues[i] = (_barValues[i] * 0.85f) + (currentBars[i] * 0.15f);
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

                            // Draw bars
                            int barWidth = (ImageWidth - (BarCount - 1) * BarSpacing) / BarCount;
                            var brush = (SolidColorBrush)Application.Current.Resources["MicaWPF.Brushes.SystemAccentColorTertiary"];

                            bool centeredBars = SettingsManager.Current.TaskbarVisualizerCenteredBars;
                            int barBaseline = SettingsManager.Current.TaskbarVisualizerBaseline ? 4 : 0;

                            int centerY = ImageHeight / 2;
                            
                            for (int i = 0; i < BarCount; i++)
                            {
                                float normalizedValue = Math.Clamp(_barValues[i], 0f, 1f);
                                int barHeight = Math.Max((int)(normalizedValue * ImageHeight), barBaseline);
                                int barX = i * (barWidth + BarSpacing);
                                
                                int barY, barEndY;
                                if (centeredBars)
                                {
                                    // Center the bars - expand up and down from middle
                                    int halfHeight = barHeight / 2;
                                    barY = centerY - halfHeight;
                                    barEndY = centerY + halfHeight;
                                }
                                else
                                {
                                    // Original behavior - bars rise from bottom
                                    barY = ImageHeight - barHeight;
                                    barEndY = ImageHeight;
                                }

                                int cornerRadius = 6;

                                for (int y = barY; y < barEndY && y < ImageHeight && y >= 0; y++)
                                {
                                    for (int x = barX; x < barX + barWidth && x < ImageWidth; x++)
                                    {
                                        float alpha = 1f;

                                        // Calculate relative position within the bar
                                        int relativeY = y - barY;
                                        int actualBarHeight = barEndY - barY;

                                        // Check corners
                                        bool inTopLeftCorner = (relativeY < cornerRadius) && (x - barX < cornerRadius);
                                        bool inTopRightCorner = (relativeY < cornerRadius) && (barX + barWidth - x <= cornerRadius);
                                        bool inBottomLeftCorner = centeredBars && (barEndY - y <= cornerRadius) && (x - barX < cornerRadius);
                                        bool inBottomRightCorner = centeredBars && (barEndY - y <= cornerRadius) && (barX + barWidth - x <= cornerRadius);

                                        if (inTopLeftCorner || inTopRightCorner || inBottomLeftCorner || inBottomRightCorner)
                                        {
                                            float dx = (inTopLeftCorner || inBottomLeftCorner) ?
                                                (cornerRadius - 0.5f - (x - barX)) :
                                                (cornerRadius - 0.5f - (barX + barWidth - x));
                                            float dy = (inTopLeftCorner || inTopRightCorner) ?
                                                (cornerRadius - 0.5f - relativeY) :
                                                (cornerRadius - 0.5f - (barEndY - y));
                                            float distance = MathF.Sqrt(dx * dx + dy * dy);

                                            if (distance > cornerRadius)
                                                continue; // Outside corner

                                            // Anti-aliasing: fade at edge
                                            if (distance > cornerRadius - 1f)
                                                alpha = cornerRadius - distance;
                                        }

                                        int index = y * stride + x * 4;
                                        if (index + 3 < buffer.Length)
                                        {
                                            buffer[index] = brush.Color.B;
                                            buffer[index + 1] = brush.Color.G;
                                            buffer[index + 2] = brush.Color.R;
                                            buffer[index + 3] = (byte)(255 * alpha);
                                        }
                                    }
                                }

                            }
                        }

                        _bitmap.AddDirtyRect(new Int32Rect(0, 0, ImageWidth, ImageHeight));
                    }
                    finally
                    {
                        _bitmap.Unlock();
                    }

                    BitmapUpdated?.Invoke(this, EventArgs.Empty);
                }
            }, System.Windows.Threading.DispatcherPriority.Render);
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
