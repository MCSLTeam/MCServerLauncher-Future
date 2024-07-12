using MCServerLauncher.UI.Helpers;
using System.Windows;
using System.Windows.Controls;

namespace MCServerLauncher.UI.View
{
    /// <summary>
    /// About.xaml 的交互逻辑
    /// </summary>
    public partial class AboutPage : Page
    {
        public AboutPage()
        {
            InitializeComponent();
        }
        private async void TestButton(object sender, RoutedEventArgs e)
        {
            await new NetworkUtils().OpenUrl("https://ys.mihoyo.com/cloud");
        }
    }
}
