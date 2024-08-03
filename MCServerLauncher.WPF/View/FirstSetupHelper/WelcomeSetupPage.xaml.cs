using MCServerLauncher.WPF.Helpers;
using System.Windows;

namespace MCServerLauncher.WPF.View.FirstSetupHelper
{
    /// <summary>
    ///     WelcomeSetupPage.xaml 的交互逻辑
    /// </summary>
    public partial class WelcomeSetupPage
    {
        public WelcomeSetupPage()
        {
            InitializeComponent();
        }

        private void Next(object sender, RoutedEventArgs e)
        {
            var parent = this.TryFindParent<FirstSetup>();
            parent.FinishSetup();
        }
    }
}
