using System;
using System.Windows;
using iNKORE.UI.WPF.Modern.Controls;
using iNKORE.UI.WPF.Modern.Media.Animation;
using MCServerLauncher.WPF.Main.View;
using Page = System.Windows.Controls.Page;

namespace MCServerLauncher.WPF.Main
{
    /// <summary>
    ///     MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly Page _createInstance = new CreateInstancePage();
        private readonly Page _help = new HelpPage();
        private readonly Page _home = new HomePage();
        private readonly Page _instanceManager = new InstanceManagerPage();
        private readonly Page _resDownload = new ResDownloadPage();
        private readonly Page _settings = new SettingsPage();

        public MainWindow()
        {
            InitializeComponent();
            CurrentPage.Navigate(_home, new DrillInNavigationTransitionInfo());
        }

        private void NavigationTriggered(NavigationView sender, NavigationViewItemInvokedEventArgs args)
        {
            if (args.IsSettingsInvoked)
                NavigateTo(typeof(int), args.RecommendedNavigationTransitionInfo);
            else if (args.InvokedItemContainer != null)
                NavigateTo(Type.GetType(args.InvokedItemContainer.Tag.ToString()),
                    args.RecommendedNavigationTransitionInfo);
        }

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
                case not null when navPageType == typeof(TestPage):
                    CurrentPage.Navigate(new TestPage());
                    break;
            }
        }
    }
}