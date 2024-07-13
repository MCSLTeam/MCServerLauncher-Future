
using iNKORE.UI.WPF.Modern.Controls;
using iNKORE.UI.WPF.Modern.Media.Animation;
using MCServerLauncher.UI.View;
using System;
using System.Windows;
using Page = System.Windows.Controls.Page;

namespace MCServerLauncher.UI
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {

        private Page Home = new HomePage();
        private Page CreateInstance = new CreateInstancePage();
        private Page InstanceManager = new InstanceManagerPage();
        private Page ResDownload = new ResDownloadPage();
        private Page Help = new HelpPage();
        private Page Settings = new SettingsPage();

        public MainWindow()
        {
            InitializeComponent();
            CurrentPage.Content = Home;
        }

        private void NavigationTriggered(NavigationView sender, NavigationViewItemInvokedEventArgs args)
        {
            if (args.IsSettingsInvoked == true)
            {
                NavigateTo(typeof(int), args.RecommendedNavigationTransitionInfo);
            }
            else if (args.InvokedItemContainer != null)
            {
                NavigateTo(Type.GetType(args.InvokedItemContainer.Tag.ToString()), args.RecommendedNavigationTransitionInfo);
            }
        }

        private void NavigateTo(Type navPageType, NavigationTransitionInfo transitionInfo)
        {
            Type preNavPageType = CurrentPage.Content.GetType();
            if (navPageType is not null && !Equals(navPageType, preNavPageType))
            {
                if (navPageType == typeof(HomePage))
                {
                    CurrentPage.Navigate(Home);
                }
                if (navPageType == typeof(CreateInstancePage))
                {
                    CurrentPage.Navigate(CreateInstance);
                }
                if (navPageType == typeof(InstanceManagerPage))
                {
                    CurrentPage.Navigate(InstanceManager);
                }
                if (navPageType == typeof(ResDownloadPage))
                {
                    CurrentPage.Navigate(ResDownload);
                }
                if (navPageType == typeof(HelpPage))
                {
                    CurrentPage.Navigate(Help);
                }
                if (navPageType == typeof(SettingsPage))
                {
                    CurrentPage.Navigate(Settings);
                }
                if (navPageType == typeof(TestPage))
                {
                    CurrentPage.Navigate(new TestPage());
                }
            }
        }
    }
}