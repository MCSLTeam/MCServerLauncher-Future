using iNKORE.UI.WPF.Modern;
using iNKORE.UI.WPF.Modern.Controls;
using MCServerLauncher.WPF.View.Components.Generic;
using Microsoft.Toolkit.Uwp.Notifications;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

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
        /// <param name="position">Position of the notification.</param>
        /// <param name="durationMs">Duration in milliseconds.</param>
        public static void Push(string title, string message, bool isClosable, InfoBarSeverity severity, 
            Constants.InfoBarPosition position = Constants.InfoBarPosition.TopRight, int durationMs = 1500)
        {
            InfoBar infoBar = new()
            {
                Title = title,
                Message = message,
                Severity = severity,
                IsClosable = isClosable,
                IsOpen = true,
                IsHitTestVisible = true,
                Effect = new DropShadowEffect
                {
                    Color = Colors.Gray,
                    Direction = 270,
                    ShadowDepth = 2,
                    Opacity = 0.4,
                    BlurRadius = 8
                }
            };
            if (severity == InfoBarSeverity.Informational) infoBar.Background = new SolidColorBrush((Color)Application.Current.Resources["SystemFillColorSolidNeutralBackground"]);
            ThemeManager.Current.ActualApplicationThemeChanged += (sender, args) =>
            {
                if (severity == InfoBarSeverity.Informational) infoBar.Background = new SolidColorBrush((Color)Application.Current.Resources["SystemFillColorSolidNeutralBackground"]);
            };
            NotificationContainer.Instance.AddNotification(infoBar, position, durationMs);
            new ToastContentBuilder()
            .AddText(title)
            .AddText(message)
            .Show();
        }
    }
}
