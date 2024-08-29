using iNKORE.UI.WPF.Modern.Controls;
using System.Windows;

namespace MCServerLauncher.WPF.View.Pages
{
    /// <summary>
    ///    NotificationCenterPage.xaml 的交互逻辑
    /// </summary>
    public partial class NotificationCenterPage
    {
        public NotificationCenterPage()
        {
            InitializeComponent();
        }

        /// <summary>
        ///    Send a notification.
        /// </summary>
        /// <param name="title">Title of the notification.</param>
        /// <param name="message">Content of the notification.</param>
        /// <param name="isClosable">Controls whether notifications can be turned off.</param>
        /// <param name="severity">Level.</param>
        public void PushNotification(string title, string message, bool isClosable, InfoBarSeverity severity)
        {
            NotificationContainer.Children.Insert(
                0,
                new InfoBar
                {
                    Title = title,
                    Message = message,
                    Severity = severity,
                    IsClosable = isClosable,
                    IsOpen = true
                }
            );
        }

        private void TestNotification(object sender, RoutedEventArgs e)
        {
            PushNotification("Test", "Test", true, InfoBarSeverity.Success);
        }
    }
}