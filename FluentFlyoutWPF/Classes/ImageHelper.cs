using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FluentFlyoutWPF.Classes
{
    public static class ImageHelper
    {
        public static Color GetDominantColor(BitmapSource bitmapSource)
        {
            
            if (bitmapSource == null)
                {
                    return Colors.Gray;
                }
            var resized = new TransformedBitmap(bitmapSource,
            new ScaleTransform(100.0 / bitmapSource.PixelWidth,
                                   100.0 / bitmapSource.PixelHeight));
            var converted = new FormatConvertedBitmap(resized, PixelFormats.Bgra32, null, 0);

            int w = converted.PixelWidth;
            int h = converted.PixelHeight;
            int stride = w * 4;
            byte[] pixels = new byte[h * stride];
            converted.CopyPixels(pixels, stride, 0);

            Color first = GetAverageColor(pixels, out int validCount);
            if (validCount == 0)   
            {
                return Colors.Gray;
            }

            if (IsNearWhiteOrBlack(first))
            {
                Color accent = GetAccentColor(pixels);
                return accent;
            }

            return first;
        }

        #region Custom Helper Methods
        private static Color GetAverageColor(byte[] pixels, out int validCount)
        {
            long r = 0, g = 0, b = 0;
            validCount = 0;
            for (int i = 0; i < pixels.Length; i += 4)
            {
                byte a = pixels[i + 3];
                if (a < 10) continue;

                b += pixels[i];
                g += pixels[i + 1];
                r += pixels[i + 2];
                validCount++;
            }

            if (validCount == 0) return Colors.Gray;
            return Color.FromRgb((byte)(r / validCount),
                                 (byte)(g / validCount),
                                 (byte)(b / validCount));
        }

        private static bool IsNearWhiteOrBlack(Color c)
        {
            byte brightness = (byte)(0.299 * c.R + 0.587 * c.G + 0.114 * c.B);
            return brightness > 210 || brightness < 45;
        }


        private static Color GetAccentColor(byte[] pixels)
        {
            var freq = new Dictionary<Color, int>();
            for (int i = 0; i < pixels.Length; i += 4)
            {
                byte a = pixels[i + 3];
                if (a < 10) continue;

                byte b = pixels[i];
                byte g = pixels[i + 1];
                byte r = pixels[i + 2];
                var c = Color.FromRgb(r, g, b);

                if (IsNearWhiteOrBlack(c)) continue;

                if (freq.TryGetValue(c, out var count))
                {
                    freq[c] = count + 1;
                }
                else
                {
                    freq[c] = 1;
                }
            }

            if (freq.Count == 0)  
                return Colors.Gray;

            return freq.OrderByDescending(kv => kv.Value).First().Key;
        }
        #endregion
    }
}