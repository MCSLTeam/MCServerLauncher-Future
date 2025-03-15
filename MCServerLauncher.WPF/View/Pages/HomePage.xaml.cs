using iNKORE.UI.WPF.Modern.Controls;
using MCServerLauncher.WPF.Modules;
using System;
using System.Windows;
using System.Windows.Controls;
using ConsoleWindow = MCServerLauncher.WPF.InstanceConsole.Window;

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
        private void ShowExceptionWindow(object sender, RoutedEventArgs e)
        {
            throw new Exception("Test Exception");
        }

        private void PushSimpleNotification(object sender, RoutedEventArgs e)
        {
            InfoBarSeverity infoBarSeverity = ((Button)sender).Content.ToString().Split('-')[0] switch
            {
                "Informational" => InfoBarSeverity.Informational,
                "Success" => InfoBarSeverity.Success,
                "Warning" => InfoBarSeverity.Warning,
                "Error" => InfoBarSeverity.Error,
                _ => InfoBarSeverity.Informational
            };

            Constants.InfoBarPosition infoBarPosition = ((Button)sender).Content.ToString().Split('-')[1] switch
            {
                "Top" => Constants.InfoBarPosition.Top,
                "TopRight" => Constants.InfoBarPosition.TopRight,
                "Bottom" => Constants.InfoBarPosition.Bottom,
                "BottomRight" => Constants.InfoBarPosition.BottomRight,
                _ => Constants.InfoBarPosition.Top
            };

            var random = new Random();
            var randomNumber = random.Next(100000, 999999).ToString();
            Notification.Push("Title", $"Message{randomNumber} - {infoBarPosition}", false, infoBarSeverity, infoBarPosition, 3000);
        }
    }
}