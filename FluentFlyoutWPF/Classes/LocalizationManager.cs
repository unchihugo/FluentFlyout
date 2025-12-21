using FluentFlyout.Classes.Settings;
using FluentFlyout.Classes.Utils;
using FluentFlyoutWPF;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;

namespace FluentFlyout.Classes;

public static class LocalizationManager
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    
    // class to hold flow direction state
    public class LocalizationState : INotifyPropertyChanged
    {
        private FlowDirection _flowDirection = FlowDirection.LeftToRight;

        public FlowDirection FlowDirection
        {
            get => _flowDirection;
            set
            {
                if (_flowDirection != value)
                {
                    _flowDirection = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public static LocalizationState Instance { get; } = new();
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
        { "čeština", "cs" },
        { "Nederlands", "nl" },
        { "français", "fr" },
        { "Deutsch", "de" },
        { "עברית", "he" },
        { "한국어", "ko" },
        { "Português (Brasil)", "pt-BR" },
        { "Русский", "ru" },
        { "Español", "es" },
        { "Türkçe", "tr" },
        { "Tiếng Việt", "vi" },
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
        Instance.FlowDirection = _rtlLanguages.Contains(languageCode)
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;

        Logger.Debug("Applied flow direction: " + Instance.FlowDirection);

        // return if there are no windows
        if (!(Application.Current != null && Application.Current.Windows != null && Application.Current.Windows.Count > 0)) return;

        // Update all existing windows
        Application.Current.Dispatcher.Invoke(() =>
    {
        foreach (Window window in Application.Current.Windows)
        {
            window.FlowDirection = Instance.FlowDirection;
        }
    });
    }
}