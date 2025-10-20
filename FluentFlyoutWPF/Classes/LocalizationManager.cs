using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF;
using System.Diagnostics;
using System.Globalization;
using System.Windows;

namespace FluentFlyout.Classes;

public static class LocalizationManager
{
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
        string languageCode = culture[..2];

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

        Debug.WriteLine("Applying localization for language: " + languageCode);

        // if English and the localization file exists, add the selected localization dictionary
        if (languageCode == "en") return;

        var localizationDictPath = $"Resources/Localization/Dictionary-{languageCode}.xaml";

        var uri = new Uri(localizationDictPath, UriKind.Relative);

        try
        {
            var resourceDict = new ResourceDictionary() { Source = uri };
            dictionaries.Add(resourceDict);
        }
        catch (Exception)
        {
            // localization file not found, do nothing and keep the default (en-US)
            Debug.WriteLine("Localization file not found for language: " + languageCode);
            return;
        }
    }
}
