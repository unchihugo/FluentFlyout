using System.Windows;
using System.Windows.Media;

namespace FluentFlyout.Classes.Utils
{
    internal static class StringWidth
    {
        private static readonly FontFamily fontFamily = new FontFamily("Segoe UI Variable, Microsoft YaHei, Microsoft JhengHei, MS Gothic");
        private static readonly Typeface typeface = new Typeface(fontFamily, new FontStyle(), FontWeights.Medium, FontStretches.Normal);

        public static double GetStringWidth(string text)
        {
            var formattedText = new FormattedText(
                text,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                14,
                Brushes.Black,
                null,
                1);

            return formattedText.Width + 8;
        }
    }
}
