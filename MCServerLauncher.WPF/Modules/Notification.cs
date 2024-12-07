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
        public static void Push(string title, string message, bool isClosable, InfoBarSeverity severity)
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
                    Color = Colors.Black,
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
            NotificationContainer.Instance.Panel.Children.Insert(0, infoBar);
            // system layer notification
            new ToastContentBuilder()
            .AddText(title)
            .AddText(message)
            .Show();
        }
    }
}
