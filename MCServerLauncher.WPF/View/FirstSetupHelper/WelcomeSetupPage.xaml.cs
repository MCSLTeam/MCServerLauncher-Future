using System.Windows;
using MCServerLauncher.WPF.Helpers;

namespace MCServerLauncher.WPF.View.FirstSetupHelper
{
    /// <summary>
    ///    WelcomeSetupPage.xaml 的交互逻辑
    /// </summary>
    public partial class WelcomeSetupPage
    {
        public WelcomeSetupPage()
        {
            InitializeComponent();
        }

        /// <summary>
        ///    Hide FirstSetupHelper
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Next(object sender, RoutedEventArgs e)
        {
            BasicUtils.SaveSetting("App.IsFirstSetupFinished", true);
            var parent = this.TryFindParent<FirstSetup>();
            parent.FinishSetup();
        }
    }
}