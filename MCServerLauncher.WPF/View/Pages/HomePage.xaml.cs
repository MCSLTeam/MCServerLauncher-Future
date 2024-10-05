using ConsoleWindow = MCServerLauncher.WPF.InstanceConsole.Window;
using static MCServerLauncher.WPF.Modules.Notification;
using System.Windows;
using iNKORE.UI.WPF.Modern.Controls;

namespace MCServerLauncher.WPF.View.Pages
{
    /// <summary>
    ///    HomePage.xaml 的交互逻辑
    /// </summary>
    public partial class HomePage
    {
        public HomePage()
        {
            InitializeComponent();
        }

        private void ShowConsoleWindow(object sender, RoutedEventArgs e)
        {
            new ConsoleWindow().Show();
        }

        private void PushSimpleNotification(object sender, RoutedEventArgs e)
        {
            PushNotification("Title", "Message", true, InfoBarSeverity.Informational);
        }
    }
}