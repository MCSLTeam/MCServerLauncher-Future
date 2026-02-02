using iNKORE.UI.WPF.Modern.Controls;
using MCServerLauncher.WPF.Modules;
using MCServerLauncher.WPF.Services.Interfaces;
using System.Windows.Controls;

namespace MCServerLauncher.WPF.Services
{
    /// <summary>
    /// Service implementation for showing notifications.
    /// </summary>
    public class NotificationService : INotificationService
    {
        public void Push(
            string title,
            string message,
            bool isClosable,
            InfoBarSeverity severity,
            Constants.InfoBarPosition position = Constants.InfoBarPosition.TopRight,
            int durationMs = 1500,
            bool systemNotify = true,
            Control? content = null,
            Button? button = null,
            bool isButtonRegisterClose = false)
        {
            Notification.Push(title, message, isClosable, severity, position, durationMs, systemNotify, content, button, isButtonRegisterClose);
        }
    }
}
