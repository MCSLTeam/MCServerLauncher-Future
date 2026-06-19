using iNKORE.UI.WPF.Modern.Controls;
using MCServerLauncher.WPF.Modules;

namespace MCServerLauncher.WPF.Services;

public class NotificationService : INotificationService
{
    public void Push(string title, string message, bool isClosable, InfoBarSeverity severity)
    {
        Notification.Push(title, message, isClosable, severity);
    }
}
