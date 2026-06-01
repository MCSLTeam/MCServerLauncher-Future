using iNKORE.UI.WPF.Modern.Controls;

namespace MCServerLauncher.WPF.Services;

public interface INotificationService
{
    void Push(string title, string message, bool isClosable, InfoBarSeverity severity);
}
