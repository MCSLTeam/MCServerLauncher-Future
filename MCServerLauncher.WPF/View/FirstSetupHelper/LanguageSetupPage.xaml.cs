using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using iNKORE.UI.WPF.Modern.Controls;
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
            if ("zh".Equals(CultureInfo.CurrentCulture.TwoLetterISOLanguageName))
            {
                LanguageManager.Instance.ChangeLanguage(new CultureInfo("zh-Hans"));
                LanguageComboBox.SelectedIndex = 0;
            }
            else
            {
                LanguageManager.Instance.ChangeLanguage(new CultureInfo("en-US"));
                LanguageComboBox.SelectedIndex = 2;
            }
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
            "zh-Hans",
            "zh-Hant",
            "en-US"
        };

        /// <summary>
        ///     Handle language combo box changes.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void LanguageChanged(object sender, SelectionChangedEventArgs e)
        {
            LanguageManager.Instance.ChangeLanguage(new CultureInfo(LanguageList.ElementAt(LanguageComboBox.SelectedIndex)));
        }
    }
}
