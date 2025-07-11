using iNKORE.UI.WPF.Modern;
using iNKORE.UI.WPF.Modern.Controls;
using MCServerLauncher.WPF.View.Components.Generic;
using Microsoft.Toolkit.Uwp.Notifications;
using System.Windows;
using System.Windows.Controls;
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
        public static void Push(string title,
            string message,
            bool isClosable,
            InfoBarSeverity severity,
            Constants.InfoBarPosition position = Constants.InfoBarPosition.TopRight,
            int durationMs = 1500,
            bool systemNotify = true,
            Control? content = null,
            Button? button = null,
            bool isButtonRegisterClose = false
        )
        {
            if (string.IsNullOrEmpty(title)) title = Lang.Tr["Tip"];
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
            if (content != null)
            {
                content.Margin = new Thickness(0, 0, 0, 15);
                if (position == Constants.InfoBarPosition.TopRight || position == Constants.InfoBarPosition.BottomRight)
                {
                    content.HorizontalAlignment = HorizontalAlignment.Right;
                }
                else if (position == Constants.InfoBarPosition.Top || position == Constants.InfoBarPosition.Bottom)
                {
                    content.HorizontalAlignment = HorizontalAlignment.Left;
                }
                infoBar.Content = content;
            }
            if (button != null)
            {
                infoBar.Content = button;
                button.Margin = new Thickness(0, 0, 0, 15);
                if (position == Constants.InfoBarPosition.TopRight || position == Constants.InfoBarPosition.BottomRight)
                {
                    button.HorizontalAlignment = HorizontalAlignment.Right;
                }
                else if (position == Constants.InfoBarPosition.Top || position == Constants.InfoBarPosition.Bottom)
                {
                    button.HorizontalAlignment = HorizontalAlignment.Left;
                }
                if (isButtonRegisterClose)
                {
                    button.Click += (s, e) =>
                    {
                        NotificationContainer.Instance.RemoveNotification(infoBar);
                    };
                }
            }

            NotificationContainer.Instance.AddNotification(infoBar, position, durationMs);
            if (systemNotify)
            {
                new ToastContentBuilder()
                .AddText(title)
                .AddText(message)
                .Show();
            }
        }
    }
}
