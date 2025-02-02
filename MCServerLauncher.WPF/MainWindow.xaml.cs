﻿using iNKORE.UI.WPF.Modern;
using iNKORE.UI.WPF.Modern.Controls;
using iNKORE.UI.WPF.Modern.Media.Animation;
using MCServerLauncher.WPF.Modules;
using MCServerLauncher.WPF.View.Components.Generic;
using MCServerLauncher.WPF.View.Pages;
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
        private readonly Page _home = new HomePage();
        private readonly Page _createInstance = new CreateInstancePage();
        private readonly Page _daemonManager = new DaemonManagerPage();
        private readonly Page _instanceManager = new InstanceManagerPage();
        private readonly Page _resDownload = new ResDownloadPage();
        private readonly Page _help = new HelpPage();
        private readonly Page _settings = new SettingsPage();

        public MainWindow()
        {
            // Set correct theme
            ThemeManager.Current.ApplicationTheme = SettingsManager.Get?.App?.Theme switch
            {
                "light" => ApplicationTheme.Light,
                "dark" => ApplicationTheme.Dark,
                _ => null
            };
            InitializeComponent();
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
            CurrentPage.Navigate(_home, new DrillInNavigationTransitionInfo());
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
                NavigateTo(typeof(int), args.RecommendedNavigationTransitionInfo);
            else if (args.InvokedItemContainer != null)
                NavigateTo(Type.GetType(args.InvokedItemContainer.Tag.ToString()),
                    args.RecommendedNavigationTransitionInfo);
        }

        /// <summary>
        ///    Navigation to a specific page.
        /// </summary>
        /// <param name="navPageType">Type of the page.</param>
        /// <param name="transitionInfo">Transition animation.</param>
        private void NavigateTo(Type navPageType, NavigationTransitionInfo transitionInfo)
        {
            var preNavPageType = CurrentPage.Content.GetType();
            if (navPageType == preNavPageType) return;
            switch (navPageType)
            {
                case not null when navPageType == typeof(HomePage):
                    CurrentPage.Navigate(_home);
                    break;
                case not null when navPageType == typeof(CreateInstancePage):
                    CurrentPage.Navigate(_createInstance);
                    break;
                case not null when navPageType == typeof(DaemonManagerPage):
                    CurrentPage.Navigate(_daemonManager);
                    break;
                case not null when navPageType == typeof(InstanceManagerPage):
                    CurrentPage.Navigate(_instanceManager);
                    break;
                case not null when navPageType == typeof(ResDownloadPage):
                    CurrentPage.Navigate(_resDownload);
                    break;
                case not null when navPageType == typeof(HelpPage):
                    CurrentPage.Navigate(_help);
                    break;
                case not null when navPageType == typeof(SettingsPage):
                    CurrentPage.Navigate(_settings);
                    break;
                case not null when navPageType == typeof(DebugPage):
                    CurrentPage.Navigate(new DebugPage());
                    break;
            }
        }

        private void ShowDownloadHistory(object sender, RoutedEventArgs e)
        {
            DownloadHistoryFlyout.ShowAt(DownloadHistoryButton);
        }
    }
}