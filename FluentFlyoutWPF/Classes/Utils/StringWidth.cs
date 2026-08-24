// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes.Settings;
using System.Windows;
using System.Windows.Media;

namespace FluentFlyout.Classes.Utils
{
    internal static class StringWidth
    {
        private static FontFamily? fontFamily;
        private static string cachedFontFamily = string.Empty;
        private static Dictionary<string, Typeface> _cachedTypefaces = new();

        /// <summary>
        /// Gets the width of the specified string when rendered with the specified font weight.
        /// </summary>
        /// <param name="text">Text to measure</param>
        /// <param name="fontWeight">Weight of the font. Defaults to 500 (Medium)</param>
        /// <param name="fontSize">Size of the font in device-independent units (pixels). Defaults to 14.</param>
        /// <returns>The width of the specified text, in device-independent units (pixels), including a small padding.</returns>
        public static double GetStringWidth(string? text, int fontWeight = 500, int fontSize = 14)
        {
            if (text == null) return 0;

            var formattedText = new FormattedText(
                text,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                GetCurrentTypeface(fontWeight),
                fontSize,
                Brushes.Black,
                null,
                1);

            return formattedText.Width;
        }

        // Returns the current Typeface based on the font family and weight, caching it for performance.
        private static Typeface GetCurrentTypeface(int fontWeight)
        {
            string currentFontFamily = SettingsManager.Current.FontFamily ?? "Segoe UI Variable, Microsoft YaHei UI, Yu Gothic UI, Malgun Gothic";

            // determine if the font family has changed since the last call, and if so, update the cached font family
            if (fontFamily == null || cachedFontFamily != currentFontFamily)
            {
                // get the current font family from user settings or use the default one
                fontFamily = new FontFamily(currentFontFamily);
                cachedFontFamily = currentFontFamily;
            }

            _cachedTypefaces.TryGetValue(currentFontFamily + fontWeight, out var cachedTypeface);

            // if exists, return the cached typeface. Otherwise, create a new one and cache it.
            if (cachedTypeface != null)
            {
                return cachedTypeface;
            }
            else
            {
                var newTypeface = new Typeface(fontFamily, new FontStyle(), fontWeight == 400 ? FontWeights.Normal : FontWeights.Medium, FontStretches.Normal);
                _cachedTypefaces[currentFontFamily + fontWeight] = newTypeface;
                return newTypeface;
            }
        }
    }
}