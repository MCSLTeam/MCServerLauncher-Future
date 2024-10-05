using iNKORE.UI.WPF.Modern.Controls;
using MCServerLauncher.WPF.View.Components.Generic;

namespace MCServerLauncher.WPF.Modules
{
    public class Notification
    {
        /// <summary>
        ///    Send a notification.
        /// </summary>
        /// <param name="title">Title of the notification.</param>
        /// <param name="message">Content of the notification.</param>
        /// <param name="isClosable">Controls whether notifications can be turned off.</param>
        /// <param name="severity">Level.</param>
        public static void PushNotification(string title, string message, bool isClosable, InfoBarSeverity severity)
        {
            NotificationCenterFlyoutContent.Instance.NotificationContainer.Children.Insert(
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
