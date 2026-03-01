using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Storage.Streams;
using Wpf.Ui.Appearance;

namespace FluentFlyoutWPF.Classes.Utils;

internal static class BitmapHelper
{
    private const int _maxThumbnailSize = 256; // previously 512, reduced for application memory
    private static List<Color>? _savedDominantColors;

    public static List<Color> SavedDominantColors
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

    // K-means clustering
    public static List<Color> GetDominantColors(BitmapImage? bitmap, int colorCount, int maxIterations = 15)
    {
        if (bitmap == null)
        {
            _savedDominantColors = [];
            return [];
        }

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

            if (a < 128) continue;           // skip transparent
            if (rng.Next(10) != 0) continue; // sample ~10%

            samples.Add([r, g, b]);
        }

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

        List<Color> result = [.. centroids.Select(c => Color.FromArgb(255, (byte)c[0], (byte)c[1], (byte)c[2]))];

        // lighten colors and add contrast when in dark mode
        if (ApplicationThemeManager.GetSystemTheme() == SystemTheme.Dark)
        {
            const double exposure = 0.35;
            const double contrast = 1.15;

            result = [.. result
                .Select(c =>
                {
                    static double ToLinear(byte v)
                        => Math.Pow(v / 255.0, 2.2);

                    static byte ToGamma(double v)
                        => (byte)Math.Clamp(Math.Pow(v, 1.0 / 2.2) * 255.0, 0, 255);

                    double r = ToLinear(c.R);
                    double g = ToLinear(c.G);
                    double b = ToLinear(c.B);

                    // perceived luminance (ITU-R BT.709)
                    double l = 0.2126 * r + 0.7152 * g + 0.0722 * b;

                    // exposure + contrast
                    l = (l + exposure - 0.5) * contrast + 0.5;
                    l = Math.Clamp(l, 0, 1);

                    double scale = l / Math.Max(0.0001, 0.2126 * r + 0.7152 * g + 0.0722 * b);

                    return Color.FromArgb(
                        c.A,
                        ToGamma(r * scale),
                        ToGamma(g * scale),
                        ToGamma(b * scale));
                })];
        }

        _savedDominantColors = result;

        return result;
    }
}