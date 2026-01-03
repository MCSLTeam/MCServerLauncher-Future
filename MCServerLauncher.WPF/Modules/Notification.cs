using iNKORE.UI.WPF.Modern;
using iNKORE.UI.WPF.Modern.Controls;
using MCServerLauncher.WPF.View.Components.Generic;
using Microsoft.Toolkit.Uwp.Notifications;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

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
            // Constants.InfoBarElementPosition? elementPosition = null,
            bool isButtonRegisterClose = false
        )
        {
            #region InfoBarBase
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
            #endregion
            #region InfoBarContent or InfoBarButton
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
            #endregion

            NotificationContainer.Instance.AddNotification(infoBar, position, durationMs);
            if (systemNotify)
            {
                ShowSystemNotification(title, message, severity);
            }
        }

        private static void ShowSystemNotification(string title, string message, InfoBarSeverity severity)
        {
            try
            {
                var osVersion = Environment.OSVersion.Version;
                
                if (osVersion.Major >= 10)
                {
                    ShowToastNotification(title, message);
                }
                else if (osVersion.Major >= 6)
                {
                    ShowBalloonNotification(title, message, severity);
                }
            }
            catch
            {
            }
        }

        private static void ShowToastNotification(string title, string message)
        {
            try
            {
                new ToastContentBuilder()
                    .AddText(title)
                    .AddText(message)
                    .Show();
            }
            catch
            {
            }
        }

        private static void ShowBalloonNotification(string title, string message, InfoBarSeverity severity)
        {
            try
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    var notifyIcon = new System.Windows.Forms.NotifyIcon
                    {
                        Visible = true,
                        Icon = System.Drawing.SystemIcons.Information,
                        BalloonTipTitle = title,
                        BalloonTipText = message
                    };

                    switch (severity)
                    {
                        case InfoBarSeverity.Error:
                            notifyIcon.BalloonTipIcon = System.Windows.Forms.ToolTipIcon.Error;
                            notifyIcon.Icon = System.Drawing.SystemIcons.Error;
                            break;
                        case InfoBarSeverity.Warning:
                            notifyIcon.BalloonTipIcon = System.Windows.Forms.ToolTipIcon.Warning;
                            notifyIcon.Icon = System.Drawing.SystemIcons.Warning;
                            break;
                        case InfoBarSeverity.Success:
                            notifyIcon.BalloonTipIcon = System.Windows.Forms.ToolTipIcon.Info;
                            notifyIcon.Icon = System.Drawing.SystemIcons.Information;
                            break;
                        default:
                            notifyIcon.BalloonTipIcon = System.Windows.Forms.ToolTipIcon.Info;
                            notifyIcon.Icon = System.Drawing.SystemIcons.Information;
                            break;
                    }

                    notifyIcon.ShowBalloonTip(3000);

                    notifyIcon.BalloonTipClosed += (s, e) =>
                    {
                        notifyIcon.Visible = false;
                        notifyIcon.Dispose();
                    };

                    var timer = new DispatcherTimer
                    {
                        Interval = TimeSpan.FromSeconds(5)
                    };
                    timer.Tick += (s, e) =>
                    {
                        timer.Stop();
                        if (notifyIcon != null)
                        {
                            notifyIcon.Visible = false;
                            notifyIcon.Dispose();
                        }
                    };
                    timer.Start();
                });
            }
            catch
            {
            }
        }
    }
}
