using System;
using System.Threading.Tasks;
using System.Windows;
using iNKORE.UI.WPF.Modern.Controls;
using iNKORE.UI.WPF.Modern.Media.Animation;
using MCServerLauncher.WPF.View;
using Page = System.Windows.Controls.Page;

namespace MCServerLauncher.WPF
{
    /// <summary>
    ///     MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly Page _home = new HomePage();
        private readonly Page _createInstance = new CreateInstancePage();
        private readonly Page _instanceManager = new InstanceManagerPage();
        private readonly Page _resDownload = new ResDownloadPage();
        private readonly Page _help = new HelpPage();
        private readonly Page _notificationCenter = new NotificationCenterPage();
        private readonly Page _settings = new SettingsPage();

        public MainWindow()
        {
            InitializeComponent();
            InitializeView();
        }
        private async void InitializeView()
        {
            SetupView.Visibility = Visibility.Hidden;
            CurrentPage.Navigate(_home, new DrillInNavigationTransitionInfo());
            await Task.Delay(1500);
            LoadingScreen.Visibility = Visibility.Hidden;
            TitleBarGrid.Visibility = Visibility.Visible;
            NavView.Visibility = Visibility.Visible;
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
                case not null when navPageType == typeof(NotificationCenterPage):
                    CurrentPage.Navigate(_notificationCenter);
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