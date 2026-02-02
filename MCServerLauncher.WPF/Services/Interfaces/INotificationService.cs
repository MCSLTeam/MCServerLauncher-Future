using iNKORE.UI.WPF.Modern.Controls;
using MCServerLauncher.WPF.Modules;
using System.Windows.Controls;

namespace MCServerLauncher.WPF.Services.Interfaces
{
    /// <summary>
    /// Service for showing notifications.
    /// </summary>
    public interface INotificationService
    {
        /// <summary>
        /// Pushes a notification to the user.
        /// </summary>
        /// <param name="title">Title of the notification.</param>
        /// <param name="message">Content of the notification.</param>
        /// <param name="isClosable">Controls whether notifications can be turned off.</param>
        /// <param name="severity">Level of severity.</param>
        /// <param name="position">Position of the notification.</param>
        /// <param name="durationMs">Duration in milliseconds.</param>
        /// <param name="systemNotify">Whether to show system notification.</param>
        /// <param name="content">Optional custom content control.</param>
        /// <param name="button">Optional button control.</param>
        /// <param name="isButtonRegisterClose">Whether button click closes notification.</param>
        void Push(
            string title,
            string message,
            bool isClosable,
            InfoBarSeverity severity,
            Constants.InfoBarPosition position = Constants.InfoBarPosition.TopRight,
            int durationMs = 1500,
            bool systemNotify = true,
            Control? content = null,
            Button? button = null,
            bool isButtonRegisterClose = false
        );
    }
}
