using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using iNKORE.UI.WPF.Modern.Controls;
using MCServerLauncher.WPF.Modules;
using MCServerLauncher.WPF.Services.Interfaces;
using MCServerLauncher.WPF.ViewModels.Base;
using System;
using ConsoleWindow = MCServerLauncher.WPF.InstanceConsole.Window;

namespace MCServerLauncher.WPF.ViewModels
{
    /// <summary>
    /// ViewModel for the HomePage.
    /// </summary>
    public partial class HomePageViewModel : ViewModelBase
    {
        private readonly INotificationService _notificationService;

        public HomePageViewModel(INotificationService notificationService)
        {
            _notificationService = notificationService;
        }

        [RelayCommand]
        private void ShowConsoleWindow()
        {
            new ConsoleWindow().Show();
        }

        [RelayCommand]
        private void ShowExceptionWindow()
        {
            throw new Exception("Test Exception");
        }

        [RelayCommand]
        private void PushNotification(string parameter)
        {
            var parts = parameter.Split('-');
            if (parts.Length != 2) return;

            var severity = parts[0] switch
            {
                "Informational" => InfoBarSeverity.Informational,
                "Success" => InfoBarSeverity.Success,
                "Warning" => InfoBarSeverity.Warning,
                "Error" => InfoBarSeverity.Error,
                _ => InfoBarSeverity.Informational
            };

            var position = parts[1] switch
            {
                "Top" => Constants.InfoBarPosition.Top,
                "TopRight" => Constants.InfoBarPosition.TopRight,
                "Bottom" => Constants.InfoBarPosition.Bottom,
                "BottomRight" => Constants.InfoBarPosition.BottomRight,
                _ => Constants.InfoBarPosition.Top
            };

            var random = new Random();
            var randomNumber = random.Next(100000, 999999).ToString();
            _notificationService.Push("Title", $"Message{randomNumber} - {position}", false, severity, position, 3000);
        }
    }
}
