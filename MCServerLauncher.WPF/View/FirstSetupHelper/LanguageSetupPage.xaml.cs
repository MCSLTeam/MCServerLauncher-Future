using MCServerLauncher.WPF.Modules;
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
            LanguageComboBox.ItemsSource = LanguageManager.LanguageNameList;
            LanguageComboBox.SelectionChanged -= LanguageChanged;
            LanguageComboBox.SelectedIndex = LanguageManager.LanguageList.IndexOf(SettingsManager.AppSettings.App.Language);
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
            parent.GoEulaSetup();
        }

        /// <summary>
        ///     Handle language combo box changes.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void LanguageChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SettingsManager.AppSettings.App.IsFirstSetupFinished) return;
            LanguageManager.Localize.ChangeLanguage(new CultureInfo(LanguageManager.LanguageList.ElementAt(LanguageComboBox.SelectedIndex)));
            SettingsManager.SaveSetting("App.Language", LanguageManager.LanguageList.ElementAt(LanguageComboBox.SelectedIndex));
            // restart this app
            System.Diagnostics.Process.Start(Application.ResourceAssembly.Location);
            Application.Current.Shutdown();
        }
    }
}
