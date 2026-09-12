// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes.Settings;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Storage.Streams;
using Wpf.Ui.Appearance;

namespace FluentFlyout.Classes.Utils;

internal static class BitmapHelper
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    // LRU cache implementation for caching thumbnails and their dominant colors
    private sealed class LruCache<TKey, TValue> where TKey : notnull
    {
        private readonly int _capacity;
        private readonly Dictionary<TKey, LinkedListNode<CacheEntry>> _map;
        private readonly LinkedList<CacheEntry> _lruList = [];
        private readonly object _sync = new();

        private sealed class CacheEntry(TKey key, TValue value)
        {
            public TKey Key { get; } = key;
            public TValue Value { get; set; } = value;
        }

        public LruCache(int capacity)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);

            _capacity = capacity;
            _map = new Dictionary<TKey, LinkedListNode<CacheEntry>>(capacity);
        }

        public bool TryGetValue(TKey key, out TValue? value)
        {
            lock (_sync)
            {
                if (_map.TryGetValue(key, out var node))
                {
                    _lruList.Remove(node);
                    _lruList.AddFirst(node);
                    value = node.Value.Value;
                    return true;
                }
            }

            value = default;
            return false;
        }

        public void Set(TKey key, TValue value)
        {
            lock (_sync)
            {
                if (_map.TryGetValue(key, out var existing))
                {
                    existing.Value.Value = value;
                    _lruList.Remove(existing);
                    _lruList.AddFirst(existing);
                    return;
                }

                var node = new LinkedListNode<CacheEntry>(new CacheEntry(key, value));
                _lruList.AddFirst(node);
                _map[key] = node;

                if (_map.Count <= _capacity)
                    return;

                var leastRecent = _lruList.Last;
                if (leastRecent == null)
                    return;

                _lruList.RemoveLast();
                _map.Remove(leastRecent.Value.Key);
            }
        }
    }

    private const int _maxThumbnailSize = 256; // previously 512, reduced for application memory
    private const int _cacheEntryLimit = 5;

    // cached thumbnails to prevent reprocessing
    private static readonly LruCache<int, BitmapImage> _thumbnailCache = new(_cacheEntryLimit);

    // cached bitmapImage hashes and their dominant colors
    private static readonly LruCache<int, List<SolidColorBrush>> _dominantColorsCache = new(_cacheEntryLimit);

    private static int _currentHashCode = 0;
    private static readonly AsyncLocal<int> _currentHashCodeContext = new();

    // current or latest dominant colors
    private static List<SolidColorBrush>? _currentDominantColors;

    // cached palette used by the taskbar visualizer (kept separate from the accent colors above)
    private static List<SolidColorBrush>? _visualizerPalette;
    private static int _visualizerPaletteHashCode;
    private static int _visualizerPaletteColorCount = -1;

    public static List<SolidColorBrush> SavedDominantColors
    {
        get => _currentDominantColors ??= [];
    }

    public static int GetStableThumbnailHash(IRandomAccessStreamReference thumbnail)
    {
        if (thumbnail == null)
            return 0;

        try
        {
            using Stream stream = thumbnail.OpenReadAsync().GetAwaiter().GetResult().AsStreamForRead();
            using SHA256 sha256 = SHA256.Create();
            byte[] hashBytes = sha256.ComputeHash(stream);
            return BitConverter.ToInt32(hashBytes, 0);
        }
        catch (Exception ex)
        {
            Logger.Info(ex, "Failed to compute thumbnail hash; falling back to object hash");
            return thumbnail.GetHashCode();
        }
    }

    internal static BitmapImage? GetThumbnail(IRandomAccessStreamReference? thumbnail, int maxThumbnailSize = _maxThumbnailSize)
    {
        if (thumbnail == null)
            return null;

        int hashCode = GetStableThumbnailHash(thumbnail);

        if (hashCode == 0)
            return null;

        if (_thumbnailCache.TryGetValue(hashCode, out var cachedImage) && cachedImage != null)
        {
            _currentHashCode = hashCode;
            _currentHashCodeContext.Value = hashCode;
            return cachedImage;
        }

        BitmapImage image = new();

        try
        {
            using (var imageStream = thumbnail.OpenReadAsync().GetAwaiter().GetResult().AsStreamForRead())
            {
                // initialize the BitmapImage
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = maxThumbnailSize;
                image.StreamSource = imageStream;
                image.EndInit();
            }
            image.Freeze();
        }
        catch (Exception ex)
        {
            Logger.Info(ex, "Failed to load thumbnail image");
            return null;
        }

        // add bitmap to thumbnail cache with empty brush
        _thumbnailCache.Set(hashCode, image);

        _currentHashCode = hashCode;
        _currentHashCodeContext.Value = hashCode;
        return image;
    }

    internal static CroppedBitmap? CropToSquare(BitmapImage? sourceImage)
    {
        if (sourceImage == null)
            return null;

        int size = (int)Math.Min(sourceImage.PixelWidth, sourceImage.PixelHeight);
        int x = (sourceImage.PixelWidth - size) / 2;
        int y = (sourceImage.PixelHeight - size) / 2;

        var rect = new Int32Rect(x, y, size, size);

        // create a CroppedBitmap (this is a lightweight object)
        var croppedBitmap = new CroppedBitmap(sourceImage, rect);

        croppedBitmap.Freeze();
        return croppedBitmap;
    }

    /// <summary>
    /// Gets dominant colors from last cached Bitmap from GetThumbnail method.
    /// K-means clustering for multiple colors, histogram peak for single color.
    /// </summary>
    /// <param name="colorCount">Amount of colors needed</param>
    /// <param name="maxIterations">Amount of k-means iterations (more = higher accuracy)</param>
    /// <returns>List of dominant colors from cached Bitmap as SolidColorBrush</returns>
    public static List<SolidColorBrush> GetDominantColors(int colorCount, int maxIterations = 15)
    {
        int hashCode = CurrentDominantColorHashCode();

        if (!SettingsManager.Current.UseAlbumArtAsAccentColor || hashCode == 0)
        {
            _currentDominantColors = GetAccentColorBrushes();
            return _currentDominantColors;
        }

        // check if we've already calculated colors for this thumbnail by checking
        // the current hash with cache (dumb method because we're assuming it's always the latest)
        if (_dominantColorsCache.TryGetValue(hashCode, out var cachedColors) && cachedColors != null)
        {
            _currentDominantColors = cachedColors;
            return _currentDominantColors;
        }

        var extracted = ExtractDominantColors(hashCode, colorCount, maxIterations);
        if (extracted.Count > 0)
        {
            _currentDominantColors = extracted;
            _dominantColorsCache.Set(hashCode, extracted);
        }

        return _currentDominantColors ?? [];
    }

    /// <summary>
    /// Palette extracted from the currently playing album art, used by the taskbar visualizer.
    /// Unlike <see cref="GetDominantColors"/> this always reads the cover art (no matter what the
    /// accent color setting says), skips the light/dark theme adjustments so the colors stay vivid,
    /// and leaves the shared dominant color cache untouched so the rest of the UI is unaffected.
    /// </summary>
    public static List<SolidColorBrush> GetVisualizerPalette(int colorCount, int maxIterations = 15)
    {
        int hashCode = CurrentDominantColorHashCode();

        if (hashCode == 0)
            return GetAccentColorBrushes(); // no cover art available

        if (_visualizerPalette != null && _visualizerPaletteHashCode == hashCode && _visualizerPaletteColorCount == colorCount)
            return _visualizerPalette;

        var palette = ExtractDominantColors(hashCode, colorCount, maxIterations, applyThemeAdjustment: false);
        if (palette.Count > 0)
        {
            // make the bars pop (album art can be very muted) and order the palette by hue so the
            // gradient across the bars is smooth instead of following the arbitrary k-means order
            palette = [.. palette
                .Select(brush => new SolidColorBrush(BoostVibrance(brush.Color)))
                .OrderBy(brush => GetHue(brush.Color))];

            foreach (var brush in palette)
                brush.Freeze();

            _visualizerPalette = palette;
            _visualizerPaletteHashCode = hashCode;
            _visualizerPaletteColorCount = colorCount;
        }

        return palette;
    }

    /// <summary>
    /// Slightly boosts saturation and lifts very dark colors so muted album art still
    /// produces visible visualizer bars.
    /// </summary>
    private static Color BoostVibrance(Color color)
    {
        double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;

        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double value = max;
        double saturation = max <= 0 ? 0 : (max - min) / max;
        double hue = GetHue(color);

        saturation = Math.Min(0.92, (saturation * 1.45) + 0.12);
        value = Math.Clamp(value, 0.4, 1.0);

        return FromHsv(hue, saturation, value);
    }

    private static double GetHue(Color color)
    {
        double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;

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

    private static Color FromHsv(double hue, double saturation, double value)
    {
        double c = value * saturation;
        double x = c * (1 - Math.Abs(((hue / 60.0) % 2) - 1));
        double m = value - c;

        double r, g, b;
        if (hue < 60) { r = c; g = x; b = 0; }
        else if (hue < 120) { r = x; g = c; b = 0; }
        else if (hue < 180) { r = 0; g = c; b = x; }
        else if (hue < 240) { r = 0; g = x; b = c; }
        else if (hue < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }

        return Color.FromArgb(255, (byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }

    private static int CurrentDominantColorHashCode()
        => _currentHashCodeContext.Value != 0 ? _currentHashCodeContext.Value : _currentHashCode;

    private static List<SolidColorBrush> GetAccentColorBrushes()
    {
        // control color (buttons, etc.)
        var accent = (SolidColorBrush)Application.Current.TryFindResource("MicaWPF.Brushes.SystemAccentColorSecondary");
        if (!accent.IsFrozen)
            accent = accent.Clone();
        accent.Freeze();

        // accent color (for non-control elements)
        var accent2 = (SolidColorBrush)Application.Current.TryFindResource("MicaWPF.Brushes.SystemAccentColorTertiary");
        if (!accent2.IsFrozen)
            accent2 = accent2.Clone();
        accent2.Freeze();

        return [accent, accent2];
    }

    private static List<SolidColorBrush> ExtractDominantColors(int hashCode, int colorCount, int maxIterations, bool applyThemeAdjustment = true)
    {
        // start timing
#if DEBUG
        Stopwatch stopwatch = Stopwatch.StartNew();
#endif

        try
        {
            // convert BitmapImage to BGRA byte array
            if (!_thumbnailCache.TryGetValue(hashCode, out var sourceBitmap) || sourceBitmap == null)
            {
                Logger.Warn($"Thumbnail cache miss while extracting dominant colors");
                return [];
            }

            var formattedBitmap = new FormatConvertedBitmap();
            formattedBitmap.BeginInit();
            formattedBitmap.Source = sourceBitmap;
            formattedBitmap.DestinationFormat = PixelFormats.Bgra32;
            formattedBitmap.EndInit();

            int width = formattedBitmap.PixelWidth;
            int height = formattedBitmap.PixelHeight;
            int stride = width * 4;

            byte[] pixels = new byte[height * stride];
            formattedBitmap.CopyPixels(pixels, stride, 0);

            // downsample pixels
            var rng = new Random();
            var samples = new List<int[]>();

            for (int i = 0; i < pixels.Length; i += 4)
            {
                byte b = pixels[i];
                byte g = pixels[i + 1];
                byte r = pixels[i + 2];
                byte a = pixels[i + 3];

                if (a < 128) continue;
                if (rng.Next(10) != 0) continue; // sample ~10%

                samples.Add([r, g, b]);
            }

            List<Color> result;

            if (colorCount == 1)
            {
                // histogram peak for single dominant color for single color extraction (~2x faster than k-means)
                const int quantBits = 4;
                const int bins = 1 << quantBits;
                var histogram = new int[bins * bins * bins];

                foreach (var pixel in samples)
                {
                    float r = pixel[0] / 255f;
                    float g = pixel[1] / 255f;
                    float b = pixel[2] / 255f;

                    float max = MathF.Max(r, MathF.Max(g, b));
                    float min = MathF.Min(r, MathF.Min(g, b));
                    float chroma = max - min;
                    float lightness = (max + min) / 2f;

                    // skip blacks, whites, and neutrals
                    if (chroma < 0.15f) continue;
                    if (lightness < 0.15f || lightness > 0.85f) continue;

                    // weight by chroma so vivid colors dominate
                    float weight = chroma * chroma;

                    int ri = pixel[0] >> (8 - quantBits);
                    int gi = pixel[1] >> (8 - quantBits);
                    int bi = pixel[2] >> (8 - quantBits);
                    histogram[ri * bins * bins + gi * bins + bi] += (int)(weight * 100);
                }

                int peakIdx = 0;
                for (int i = 1; i < histogram.Length; i++)
                    if (histogram[i] > histogram[peakIdx]) peakIdx = i;

                int pr = peakIdx / (bins * bins);
                int pg = (peakIdx / bins) % bins;
                int pb = peakIdx % bins;

                // map each bin index back to the center of its value range
                byte peakR = (byte)((pr << (8 - quantBits)) + (1 << (8 - quantBits - 1)));
                byte peakG = (byte)((pg << (8 - quantBits)) + (1 << (8 - quantBits - 1)));
                byte peakB = (byte)((pb << (8 - quantBits)) + (1 << (8 - quantBits - 1)));

                result = [Color.FromArgb(255, peakR, peakG, peakB)];
            }
            else
            {
                // get random initial centroids
                var centroids = samples
                    .OrderBy(_ => rng.Next())
                    .Take(colorCount)
                    .Select(p => new double[] { p[0], p[1], p[2] })
                    .ToList();

                // k-means iterations
                for (int iter = 0; iter < maxIterations; iter++)
                {
                    var clusters = Enumerable.Range(0, colorCount)
                        .Select(_ => new List<int[]>())
                        .ToList();

                    // assign pixels to nearest centroid
                    foreach (var pixel in samples)
                    {
                        int best = 0;
                        double bestDist = double.MaxValue;

                        for (int i = 0; i < colorCount; i++)
                        {
                            double dr = pixel[0] - centroids[i][0];
                            double dg = pixel[1] - centroids[i][1];
                            double db = pixel[2] - centroids[i][2];
                            double dist = dr * dr + dg * dg + db * db;

                            if (dist < bestDist) { bestDist = dist; best = i; }
                        }

                        clusters[best].Add(pixel);
                    }

                    // recalculate centroids + check convergence
                    bool converged = true;
                    for (int i = 0; i < colorCount; i++)
                    {
                        if (clusters[i].Count == 0) continue;

                        double newR = clusters[i].Average(p => p[0]);
                        double newG = clusters[i].Average(p => p[1]);
                        double newB = clusters[i].Average(p => p[2]);

                        double dr = newR - centroids[i][0];
                        double dg = newG - centroids[i][1];
                        double db = newB - centroids[i][2];

                        if (dr * dr + dg * dg + db * db > 1.0) converged = false;

                        centroids[i][0] = newR;
                        centroids[i][1] = newG;
                        centroids[i][2] = newB;
                    }

                    if (converged) break;
                }

                result = [.. centroids.Select(c => Color.FromArgb(255, (byte)c[0], (byte)c[1], (byte)c[2]))];
            }

            if (applyThemeAdjustment && ApplicationThemeManager.GetSystemTheme() == SystemTheme.Dark)
            {
                // lighten colors and add contrast when in dark mode
                result = [.. result
                .Select(c =>
                {
                    double r = ToLinear(c.R);
                    double g = ToLinear(c.G);
                    double b = ToLinear(c.B);

                    double luminance = 0.2126 * r + 0.7152 * g + 0.0722 * b;

                    // lift colors that are too dark for black backgrounds
                    double targetL = Math.Max(luminance, 0.75);
                    double scale = targetL / Math.Max(0.0001, luminance);
                    r *= scale; g *= scale; b *= scale;

                    // desaturate
                    double desaturation = 0.35;
                    double newL = 0.2126 * r + 0.7152 * g + 0.0722 * b;
                    r += (newL - r) * desaturation;
                    g += (newL - g) * desaturation;
                    b += (newL - b) * desaturation;

                    return Color.FromArgb(c.A, ToGamma(r), ToGamma(g), ToGamma(b));
                })];
            }
            else if (applyThemeAdjustment)
            {
                // just desaturate when in light mode
                result = [.. result
            .Select(c =>
            {
                double r = ToLinear(c.R);
                double g = ToLinear(c.G);
                double b = ToLinear(c.B);

                double desaturation = 0.35;
                double newL = 0.2126 * r + 0.7152 * g + 0.0722 * b;
                r += (newL - r) * desaturation;
                g += (newL - g) * desaturation;
                b += (newL - b) * desaturation;

                return Color.FromArgb(c.A, ToGamma(r), ToGamma(g), ToGamma(b));
            })];
            }

            // convert to brushes
            var brushes = result.Select(c =>
            {
                var brush = new SolidColorBrush(c);
                brush.Freeze(); // makes it immutable & thread-safe
                return brush;
            }).ToList();

#if DEBUG
            stopwatch.Stop();
            Logger.Debug($"Dominant color extraction took {stopwatch.Elapsed.TotalMilliseconds} ms");
#endif
            return brushes;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error extracting dominant colors");
            return [];
        }
    }

    private static double ToLinear(byte v)
        => Math.Pow(v / 255.0, 2.2);

    private static byte ToGamma(double v)
        => (byte)Math.Clamp(Math.Pow(v, 1.0 / 2.2) * 255.0, 0, 255);
}