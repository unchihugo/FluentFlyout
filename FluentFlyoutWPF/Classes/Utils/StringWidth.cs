using System.Windows;
using System.Windows.Media;

namespace FluentFlyout.Classes.Utils
{
    internal static class StringWidth
    {
        private static readonly FontFamily fontFamily = new FontFamily("Segoe UI Variable, Microsoft YaHei, Microsoft JhengHei, MS Gothic");
        private static readonly Typeface normalTypeface = new Typeface(fontFamily, new FontStyle(), FontWeights.Normal, FontStretches.Normal);
        private static readonly Typeface mediumTypeface = new Typeface(fontFamily, new FontStyle(), FontWeights.Medium, FontStretches.Normal);

        /// <summary>
        /// Gets the width of the specified string when rendered with the specified font weight.
        /// </summary>
        /// <param name="text">Text to measure</param>
        /// <param name="fontWeight">Weight of the font. Defaults to 500 (Medium)</param>
        /// <param name="fontSize">Size of the font in device-independent units (pixels). Defaults to 14.</param>
        /// <returns>The width of the specified text, in device-independent units (pixels), including a small padding.</returns>
        public static double GetStringWidth(string text, int fontWeight = 500, int fontSize = 14)
        {
            var formattedText = new FormattedText(
                text,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                fontWeight == 400 ? normalTypeface : mediumTypeface,
                fontSize,
                Brushes.Black,
                null,
                1);

            return formattedText.Width + 8;
        }
    }
}
