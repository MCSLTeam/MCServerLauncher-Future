using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MCServerLauncher.WPF.Helpers;
using Page = System.Windows.Controls.Page;

namespace MCServerLauncher.WPF.View.FirstSetupHelper
{
    /// <summary>
    /// LanguageSetupPage.xaml 的交互逻辑
    /// </summary>
    public partial class LanguageSetupPage : Page
    {
        public LanguageSetupPage()
        {
            InitializeComponent();
            LanguageComboBox.ItemsSource = LanguageNameList;
            LanguageComboBox.SelectedIndex = 28;
        }
        /// <summary>
        ///    Go to Eula page.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Next(object sender, RoutedEventArgs e)
        {
            var parent = this.TryFindParent<FirstSetup>();
            parent.GoEulaSetup();
        }
        private static readonly List<string> LanguageList = new()
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
        private static readonly List<string> LanguageNameList = new()
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
        /// <summary>
        ///     Handle language combo box changes.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void LanguageChanged(object sender, SelectionChangedEventArgs e)
        {
            if (BasicUtils.AppSettings.App.IsFirstSetupFinished) return;
            LanguageManager.Instance.ChangeLanguage(new CultureInfo(LanguageList.ElementAt(LanguageComboBox.SelectedIndex)));
            BasicUtils.SaveSetting("App.Language", LanguageList.ElementAt(LanguageComboBox.SelectedIndex));
        }
    }
}
