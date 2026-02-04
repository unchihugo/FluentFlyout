using FluentFlyout.Classes.Settings;
using FluentFlyout.Classes.Utils;
using FluentFlyoutWPF;
using System.Globalization;
using System.Windows;

namespace FluentFlyout.Classes;

public static class LocalizationManager
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    public static double maxLength = 0;

    // current language code (first two letters) for easy access
    public static string LanguageCode { get; set; } = string.Empty;

    // dictionary of supported languages where key is the local language name and value is the language/culture code
    // check https://simplelocalize.io/data/locales/ for additional language info
    private static readonly Dictionary<string, string> _supportedLanguages = new()
    {
        { "System", "system" },
        { "English", "en-US" },
        { "العربية", "ar" },
        { "中文（简体）", "zh-CN" },
        { "中文（繁體）", "zh-TW" },
        { "hrvatski jezik", "hr" },
        { "čeština", "cs" },
        { "Nederlands", "nl" },
        { "suomi", "fi" },
        { "français", "fr" },
        { "Deutsch", "de" },
        { "עברית", "he" },
        { "Bahasa Indonesia", "id" },
        { "Italiano", "it" },
        { "日本語", "ja" },
        { "한국어", "ko" },
        { "polski", "pl" },
        { "Português (Brasil)", "pt-BR" },
        { "Русский", "ru" },
        { "Español", "es" },
        { "Türkçe", "tr" },
        { "Українська", "uk" },
        { "Tiếng Việt", "vi" },
    };

    // dictionary of font families for specific languages, priorities are switched around
    private static readonly Dictionary<string, string> _languageFontFamilies = new()
    {
        { "default", "Segoe UI Variable, Microsoft YaHei UI, Yu Gothic UI, Malgun Gothic" }, // default support for multiple languages
        //{ "zh-CN", "Segoe UI Variable, Microsoft YaHei UI, Yu Gothic UI, Malgun Gothic" }, // same as default
        { "zh-TW", "Segoe UI Variable, Microsoft JhengHei UI, Yu Gothic UI, Malgun Gothic" },
        { "ja", "Segoe UI Variable, Yu Gothic UI, Microsoft YaHei UI, Malgun Gothic" },
        { "ko", "Segoe UI Variable, Malgun Gothic, Microsoft YaHei UI, Yu Gothic UI" },
    };

    // right-to-left languages
    private static readonly HashSet<string> _rtlLanguages = ["ar", "he"];

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
        LanguageCode = languageCode;

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

        Logger.Debug("Applying localization for language: " + culture);

        // change flow direction of all windows
        ApplyFlowDirection(languageCode);

        ApplyFontFamily(culture);

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

            try
            {
                localizationDictPath = $"Resources/Localization/Dictionary-{languageCode}.xaml";
                uri = new Uri(localizationDictPath, UriKind.Relative);

                var resourceDict = new ResourceDictionary() { Source = uri };
                dictionaries.Add(resourceDict);
            }
            catch
            {
                // do nothing and keep the default (en-US)
                Logger.Warn("Localization file not found for language: " + culture);
                return;
            }
        }
        //Calculate the Lock Key Flyout text's Max Lenght
        List<double> Lengths = new List<double>();

        Lengths.Add(StringWidth.GetStringWidth(Application.Current.Resources["LockWindow_InsertPressed"].ToString()));

        var On = Application.Current.Resources["LockWindow_LockOn"].ToString();
        var Off = Application.Current.Resources["LockWindow_LockOff"].ToString();
        var OnOffMax = On.Length >= Off.Length ? On + " " : Off + " ";

        Lengths.Add(StringWidth.GetStringWidth(OnOffMax + Application.Current.Resources["LockWindow_CapsLock"].ToString()));
        Lengths.Add(StringWidth.GetStringWidth(OnOffMax + Application.Current.Resources["LockWindow_NumLock"].ToString()));
        Lengths.Add(StringWidth.GetStringWidth(OnOffMax + Application.Current.Resources["LockWindow_ScrollLock"].ToString()));

        maxLength = Lengths.Max();
    }

    private static void ApplyFlowDirection(string languageCode)
    {
        SettingsManager.Current.FlowDirection = _rtlLanguages.Contains(languageCode)
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;

        Logger.Debug("Applied flow direction: " + SettingsManager.Current.FlowDirection);
    }

    private static void ApplyFontFamily(string culture)
    {
        string fontFamily;
        if (_languageFontFamilies.TryGetValue(culture, out string? value))
        {
            fontFamily = value;
        }
        else if (_languageFontFamilies.TryGetValue(LanguageCode, out string? value1))
        {
            fontFamily = value1;
        }
        else
        {
            fontFamily = _languageFontFamilies["default"];
        }
        SettingsManager.Current.FontFamily = fontFamily;

        Logger.Debug("Applied font family: " + fontFamily);
    }
}