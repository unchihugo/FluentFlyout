using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF;
using System.Diagnostics;
using System.Globalization;
using System.Windows;

namespace FluentFlyout.Classes;

public static class LocalizationManager
{
    // dictionary of supported languages where key is the local language name and value is the language/culture code
    private static readonly Dictionary<string, string> _supportedLanguages = new()
    {
        { "System", "system" },
        { "English", "en-US" },
        { "简体中文", "zh-CN" },
        { "Nederlands", "nl" },
        { "עברית", "he" },
        { "한국어", "ko" },
        { "Português (Brasil)", "pt-BR" },
        { "Русский", "ru" },
        { "Español", "es" },
        { "Türkçe", "tr" },
        { "Tiếng Việt", "vi" },
    };

    // readonly property to access supported languages
    public static Dictionary<string, string> SupportedLanguages => _supportedLanguages;

    public static void ApplyLocalization()
    {
        string culture;
        if (SettingsManager.Current.AppLanguage == "system")
        {
            culture = CultureInfo.CurrentUICulture.Name;
        }
        else
        {
            culture = SettingsManager.Current.AppLanguage;
        }

        // extract only the language code (first two letters) from the culture
        string languageCode = culture[..Math.Min(2, culture.Length)];

        // get current localization 
        var dictionaries = App.Current.Resources.MergedDictionaries;

        // remove all localization dictionaries except the default one (en-US)
        foreach (var dictionary in dictionaries.ToList())
        {
            if (dictionary.Source != null
                && dictionary.Source.OriginalString.StartsWith("Resources/Localization/")
                && !dictionary.Source.OriginalString.EndsWith("Dictionary-en-US.xaml"))
            {
                dictionaries.Remove(dictionary);
            }
        }

        Debug.WriteLine("Applying localization for language: " + culture);

        // if English, the default (en-US) is already loaded, so no need to add another dictionary
        if (languageCode == "en") return;

        // find the localization file path based on the first two letters of the language code
        string? localizationDictPath = $"Resources/Localization/Dictionary-{culture}.xaml";

        var uri = new Uri(localizationDictPath, UriKind.Relative);

        try
        {
            var resourceDict = new ResourceDictionary() { Source = uri };
            dictionaries.Add(resourceDict);
        }
        catch (Exception)
        {
            // localization file not found, try simplified language code instead

            try {
                localizationDictPath = $"Resources/Localization/Dictionary-{languageCode}.xaml";
                uri = new Uri(localizationDictPath, UriKind.Relative);

                var resourceDict = new ResourceDictionary() { Source = uri };
                dictionaries.Add(resourceDict);
            } 
            catch 
            {
                // do nothing and keep the default (en-US)
                Debug.WriteLine("Localization file not found for language: " + culture);
                return;
            }
        }
    }
}
