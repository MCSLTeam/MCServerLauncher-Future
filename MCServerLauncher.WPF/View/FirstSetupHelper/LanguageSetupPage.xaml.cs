using MCServerLauncher.WPF.Modules;
using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
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
            LanguageComboBox.ItemsSource = Lang.LanguageNameList;
            LanguageComboBox.SelectionChanged -= LanguageChanged;
            LanguageComboBox.SelectedIndex = Lang.LanguageList.IndexOf(SettingsManager.Get?.App?.Language);
            LanguageComboBox.SelectionChanged += LanguageChanged;
        }
        /// <summary>
        ///    Go to Eula page.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Next(object sender, RoutedEventArgs e)
        {
            var parent = this.TryFindParent<FirstSetup>();
            parent?.GoEulaSetup();
        }

        /// <summary>
        ///     Handle language combo box changes.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void LanguageChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SettingsManager.Get?.App != null && SettingsManager.Get.App.IsFirstSetupFinished) return;
            Lang.Tr.ChangeLanguage(new CultureInfo(Lang.LanguageList.ElementAt(LanguageComboBox.SelectedIndex) ?? throw new InvalidOperationException()));
            SettingsManager.SaveSetting("App.Language", Lang.LanguageList.ElementAt(LanguageComboBox.SelectedIndex));
            // restart this app
            Process.Start(Application.ResourceAssembly.Location);
            Environment.Exit(0);
        }
    }
}
