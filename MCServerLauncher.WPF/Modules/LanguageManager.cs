using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace MCServerLauncher.WPF.Modules
{
    public class LanguageManager : INotifyPropertyChanged
    {
        private readonly ResourceManager _resourceManager = new("MCServerLauncher.WPF.Resources.Language", typeof(LanguageManager).Assembly);
        private static readonly Lazy<LanguageManager> Lazy = new(() => new LanguageManager());
        public static LanguageManager Localize => Lazy.Value;
        public event PropertyChangedEventHandler PropertyChanged;

        public string this[string name]
        {
            get
            {
                if (name == null)
                {
                    throw new ArgumentNullException(nameof(name));
                }
                return _resourceManager.GetString(name)!.Replace("\\n", "\n");
            }
        }

        public void ChangeLanguage(CultureInfo cultureInfo)
        {
            CultureInfo.CurrentCulture = cultureInfo;
            CultureInfo.CurrentUICulture = cultureInfo;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("item[]"));
        }

        public static readonly List<string> LanguageList = new()
        {
            "af-ZA",
            "ar-SA",
            "ca-ES",
            "cs-CZ",
            "da-DK",
            "de-DE",
            "el-GR",
            "en-US",
            "es-ES",
            "fi-FI",
            "fr-FR",
            "he-IL",
            "hu-HU",
            "it-IT",
            "ja-JP",
            "ko-KR",
            "nl-NL",
            "nb-NO",
            "pl-PL",
            "pt-BR",
            "pt-PT",
            "ro-RO",
            "ru-RU",
            "sv-SE",
            "tr-TR",
            "uk-UA",
            "vi-VN",
            "zh-Hans",
            "zh-Hant"
        };

        public static readonly List<string> LanguageNameList = new()
        {
            "Suid-Afrikaanse Nederlands",
            "العربية",
            "Català",
            "Čeština",
            "Dansk",
            "Deutsch",
            "Ελληνικά",
            "English",
            "Español",
            "Suomi",
            "Français",
            "עברית",
            "Magyar",
            "Italiano",
            "日本語",
            "한국어",
            "Nederlands",
            "Norsk",
            "Polski",
            "Português (Brasil)",
            "Português (Portugal)",
            "Română",
            "Русский",
            "Svenska",
            "Türkçe",
            "Українська",
            "Tiếng Việt",
            "简体中文",
            "繁體中文"
        };

    }
}
