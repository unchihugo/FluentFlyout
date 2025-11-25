using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace FluentFlyout.Classes.Utils
{
    internal static class StringWidth
    {
        public static double GetStringWidth(string text)
        {
            var fontFamily = new FontFamily("Segoe UI Variable, Microsoft YaHei, Microsoft JhengHei, MS Gothic");
            var typeface = new Typeface(fontFamily, new FontStyle(), FontWeights.Medium, FontStretches.Normal);

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
