using iNKORE.UI.WPF.Modern;
using iNKORE.UI.WPF.Modern.Controls;
using iNKORE.UI.WPF.Modern.Media.Animation;
using MCServerLauncher.WPF.Modules;
using MCServerLauncher.WPF.Services.Interfaces;
using MCServerLauncher.WPF.View.Components.Generic;
using MCServerLauncher.WPF.View.Pages;
using MCServerLauncher.WPF.ViewModels;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using Page = System.Windows.Controls.Page;

namespace MCServerLauncher.WPF
{
    /// <summary>
    ///    MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow
    {
        private readonly INavigationService _navigationService;
        private readonly ISettingsService _settingsService;

        public MainWindow(
            INavigationService navigationService,
            ISettingsService settingsService,
            MainWindowViewModel viewModel)
        {
            _navigationService = navigationService;
            _settingsService = settingsService;

            // Set correct theme
            ThemeManager.Current.ApplicationTheme = _settingsService.CurrentSettings?.App?.Theme switch
            {
                "light" => ApplicationTheme.Light,
                "dark" => ApplicationTheme.Dark,
                _ => null
            };

            InitializeComponent();
            DataContext = viewModel;
            InitializeView();
        }

        /// <summary>
        ///    Determine which component to show.
        /// </summary>
        private async void InitializeView()
        {
            DownloadHistoryFlyout.Content = DownloadHistoryFlyoutContent.Instance;
            GlobalGrid.Children.Add(NotificationContainer.Instance);
            Grid.SetRow(NotificationContainer.Instance, 1);
            SetupView.Visibility = Visibility.Hidden;

            // Set navigation frame
            _navigationService.SetNavigationFrame(CurrentPage);

            // Navigate to home
            _navigationService.NavigateTo<HomePageViewModel>();

            await Task.Delay(1500);
            var fadeOutAnimation = new DoubleAnimation
            {
                From = 1.0,
                To = 0.0,
                Duration = new Duration(TimeSpan.FromSeconds(0.4)),
                FillBehavior = FillBehavior.HoldEnd
            };
            fadeOutAnimation.Completed += (s, e) =>
            {
                LoadingScreen.Visibility = Visibility.Hidden;
                TitleBarGrid.Visibility = Visibility.Visible;
                if (SettingsManager.Get?.App != null && !SettingsManager.Get.App.IsFirstSetupFinished)
                {
                    SetupView.Visibility = Visibility.Visible;
                    return;
                }
                NavView.Visibility = Visibility.Visible;
                TitleBarRootBorder.Visibility = Visibility.Visible;
            };
            LoadingScreen.BeginAnimation(OpacityProperty, fadeOutAnimation);
        }

        /// <summary>
        ///    Navigation trigger handler.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void NavigationTriggered(NavigationView sender, NavigationViewItemInvokedEventArgs args)
        {
            if (args.IsSettingsInvoked)
            {
                _navigationService.NavigateTo<SettingsPageViewModel>();
            }
            else if (args.InvokedItemContainer != null && args.InvokedItemContainer.Tag != null)
            {
                NavigateByTag(args.InvokedItemContainer.Tag.ToString()!);
            }
        }

        private void NavigateByTag(string tag)
        {
            switch (tag)
            {
                case "MCServerLauncher.WPF.View.Pages.HomePage":
                    _navigationService.NavigateTo<HomePageViewModel>();
                    break;
                case "MCServerLauncher.WPF.View.Pages.CreateInstancePage":
                    _navigationService.NavigateTo<CreateInstancePageViewModel>();
                    break;
                case "MCServerLauncher.WPF.View.Pages.DaemonManagerPage":
                    _navigationService.NavigateTo<DaemonManagerPageViewModel>();
                    break;
                case "MCServerLauncher.WPF.View.Pages.InstanceManagerPage":
                    _navigationService.NavigateTo<InstanceManagerPageViewModel>();
                    break;
                case "MCServerLauncher.WPF.View.Pages.ResDownloadPage":
                    _navigationService.NavigateTo<ResDownloadPageViewModel>();
                    break;
                case "MCServerLauncher.WPF.View.Pages.HelpPage":
                    _navigationService.NavigateTo<HelpPageViewModel>();
                    break;
                case "MCServerLauncher.WPF.View.Pages.DebugPage":
                    // DebugPage is not yet migrated to MVVM, navigate directly
                    CurrentPage.Navigate(new DebugPage());
                    break;
            }
        }

        public void ToggleNavBarVisibility()
        {
            NavView.IsPaneVisible = !NavView.IsPaneVisible;
            NavView.IsPaneOpen = false;
        }

        private void ShowDownloadHistory(object sender, RoutedEventArgs e)
        {
            DownloadHistoryFlyout.ShowAt(DownloadHistoryButton);
        }
    }
}