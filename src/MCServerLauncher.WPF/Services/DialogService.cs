using System;
using System.Threading.Tasks;
using System.Windows.Threading;
using iNKORE.UI.WPF.Modern.Controls;

namespace MCServerLauncher.WPF.Services;

public class DialogService : IDialogService
{
    public async Task<ContentDialogResult> ShowConfirmAsync(string title, string content, string primaryButtonText, string closeButtonText)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = content,
            PrimaryButtonText = primaryButtonText,
            CloseButtonText = closeButtonText,
            DefaultButton = ContentDialogButton.Close
        };
        return await dialog.ShowAsync();
    }

    public async Task<ContentDialogResult> ShowCountdownConfirmAsync(string title, string content, string primaryButtonText, string closeButtonText, int countdownSeconds = 5)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = content,
            PrimaryButtonText = $"{primaryButtonText} ({countdownSeconds}s)",
            CloseButtonText = closeButtonText,
            DefaultButton = ContentDialogButton.Close,
            IsPrimaryButtonEnabled = false
        };

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        int countdown = countdownSeconds;

        timer.Tick += (s, args) =>
        {
            countdown--;
            if (countdown > 0)
            {
                dialog.PrimaryButtonText = $"{primaryButtonText} ({countdown}s)";
            }
            else
            {
                timer.Stop();
                dialog.PrimaryButtonText = primaryButtonText;
                dialog.IsPrimaryButtonEnabled = true;
            }
        };
        timer.Start();

        var result = await dialog.ShowAsync();
        timer.Stop();
        return result;
    }
}
