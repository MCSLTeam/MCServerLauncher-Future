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
            LanguageComboBox.ItemsSource = LanguageManager.LanguageNameList;
            LanguageComboBox.SelectionChanged -= LanguageChanged;
            LanguageComboBox.SelectedIndex = LanguageManager.LanguageList.IndexOf(BasicUtils.AppSettings.App.Language);
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
            if (BasicUtils.AppSettings.App.IsFirstSetupFinished) return;
            LanguageManager.Instance.ChangeLanguage(new CultureInfo(LanguageManager.LanguageList.ElementAt(LanguageComboBox.SelectedIndex)));
            BasicUtils.SaveSetting("App.Language", LanguageManager.LanguageList.ElementAt(LanguageComboBox.SelectedIndex));
            // restart this app
            System.Diagnostics.Process.Start(Application.ResourceAssembly.Location);
            Application.Current.Shutdown();
        }
    }
}
