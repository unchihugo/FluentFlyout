using FluentFlyout.Classes.Settings;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Storage.Streams;
using Wpf.Ui.Appearance;

namespace FluentFlyoutWPF.Classes.Utils;

internal static class BitmapHelper
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    
    private const int _maxThumbnailSize = 256; // previously 512, reduced for application memory
    private static List<SolidColorBrush>? _savedDominantColors;

    public static List<SolidColorBrush> SavedDominantColors
    {
        get => _savedDominantColors ??= [];
    }

    internal static BitmapImage? GetThumbnail(IRandomAccessStreamReference Thumbnail, int maxThumbnailSize = _maxThumbnailSize)
    {
        if (Thumbnail == null)
            return null;

        BitmapImage image = new();
        using (var imageStream = Thumbnail.OpenReadAsync().GetAwaiter().GetResult().AsStreamForRead())
        {
            // initialize the BitmapImage
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = maxThumbnailSize;
            image.StreamSource = imageStream;
            image.EndInit();
        }
        image.Freeze();
        return image;
    }

    internal static CroppedBitmap? CropToSquare(BitmapImage sourceImage)
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

    // K-means clustering for multiple colors, histogram peak for single color
    public static List<SolidColorBrush> GetDominantColors(BitmapImage? bitmap, int colorCount, int maxIterations = 15)
    {
        if (!SettingsManager.Current.UseAlbumArtAsAccentColor)
        {
            _savedDominantColors = [(SolidColorBrush)Application.Current.Resources["MicaWPF.Brushes.SystemAccentColorTertiary"]];
            return _savedDominantColors;
        }

        if (bitmap == null)
        {
            _savedDominantColors = [];
            return [];
        }

#if DEBUG
        // start timing
        Stopwatch stopwatch = Stopwatch.StartNew();
#endif

        // convert BitmapImage to BGRA byte array
        var formattedBitmap = new FormatConvertedBitmap();
        formattedBitmap.BeginInit();
        formattedBitmap.Source = bitmap;
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

        // lighten colors and add contrast when in dark mode
        if (ApplicationThemeManager.GetSystemTheme() == SystemTheme.Dark)
        {
            double exposure = 0.55;
            double contrast = 1.10;

            result = [.. result
                .Select(c =>
                {
                    double r = ToLinear(c.R);
                    double g = ToLinear(c.G);
                    double b = ToLinear(c.B);

                    double originalL = 0.2126 * r + 0.7152 * g + 0.0722 * b;

                    // lift dark colors
                    double liftedL = Math.Max(originalL, 0.08);

                    // exposure + contrast applied to lifted luminance
                    double targetL = (liftedL + exposure - 0.5) * contrast + 0.5;
                    targetL = Math.Clamp(targetL, 0, 1);

                    double scale = targetL / Math.Max(0.0001, originalL);

                    // cap scale to prevent oversaturation on very dark colors
                    scale = Math.Min(scale, 4.0);

                    r *= scale;
                    g *= scale;
                    b *= scale;

                    double scaleFactor = Math.Clamp((scale - 1.0) / 3.0, 0, 1);
                    double newL = 0.2126 * r + 0.7152 * g + 0.0722 * b;
                    r = r + (newL - r) * scaleFactor * 0.35;
                    g = g + (newL - g) * scaleFactor * 0.35;
                    b = b + (newL - b) * scaleFactor * 0.35;

                    return Color.FromArgb(c.A, ToGamma(r), ToGamma(g), ToGamma(b));
                })];
        }

        // convert to brushes
        var brushes = result.Select(c => new SolidColorBrush(c)).ToList();
        _savedDominantColors = brushes;

#if DEBUG
        stopwatch.Stop();
        Logger.Debug($"Dominant color extraction took {stopwatch.Elapsed.TotalMilliseconds} ms");
#endif

        return _savedDominantColors;
    }

    static double ToLinear(byte v)
        => Math.Pow(v / 255.0, 2.2);

    static byte ToGamma(double v)
        => (byte)Math.Clamp(Math.Pow(v, 1.0 / 2.2) * 255.0, 0, 255);
}