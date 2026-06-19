using System.Threading.Tasks;
using iNKORE.UI.WPF.Modern.Controls;

namespace MCServerLauncher.WPF.Services;

public interface IDialogService
{
    Task<ContentDialogResult> ShowConfirmAsync(string title, string content, string primaryButtonText, string closeButtonText);
    Task<ContentDialogResult> ShowCountdownConfirmAsync(string title, string content, string primaryButtonText, string closeButtonText, int countdownSeconds = 5);
}
