
using iNKORE.UI.WPF.Modern.Controls;
using iNKORE.UI.WPF.Modern.Media.Animation;
using MCServerLauncher.WPF.Main.View;
using System;
using System.Windows;
using Page = System.Windows.Controls.Page;

namespace MCServerLauncher.WPF.Main
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
            CurrentPage.Navigate(Home, new DrillInNavigationTransitionInfo());
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
                switch (navPageType)
                {
                    case Type t when t == typeof(HomePage):
                        CurrentPage.Navigate(Home);
                        break;
                    case Type t when t == typeof(CreateInstancePage):
                        CurrentPage.Navigate(CreateInstance);
                        break;
                    case Type t when t == typeof(InstanceManagerPage):
                        CurrentPage.Navigate(InstanceManager);
                        break;
                    case Type t when t == typeof(ResDownloadPage):
                        CurrentPage.Navigate(ResDownload);
                        break;
                    case Type t when t == typeof(HelpPage):
                        CurrentPage.Navigate(Help);
                        break;
                    case Type t when t == typeof(SettingsPage):
                        CurrentPage.Navigate(Settings);
                        break;
                    case Type t when t == typeof(TestPage):
                        CurrentPage.Navigate(new TestPage());
                        break;
                }
            }
        }
    }
}