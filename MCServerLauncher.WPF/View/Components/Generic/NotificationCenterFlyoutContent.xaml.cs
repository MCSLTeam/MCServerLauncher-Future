using iNKORE.UI.WPF.Modern.Controls;
using System.Windows.Controls;

namespace MCServerLauncher.WPF.View.Components.Generic
{
    /// <summary>
    ///     NotificationCenterFlyoutContent.xaml 的交互逻辑
    /// </summary>
    public partial class NotificationCenterFlyoutContent
    {
        public NotificationCenterFlyoutContent()
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
    }
}
