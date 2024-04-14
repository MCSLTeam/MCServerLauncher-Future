
using iNKORE.UI.WPF.Modern.Controls;
using System.Windows;
using iNKORE.UI.WPF.Modern.Media.Animation;
using iNKORE.UI.WPF.Modern;
using System;
using MCServerLauncher.View;
using Page = System.Windows.Controls.Page;

namespace MCServerLauncher
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
        private Page About = new AboutPage();

        public MainWindow()
        {
            InitializeComponent();
            frame.Content = Home;
        }

        //导航栏
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
            Type preNavPageType = frame.Content.GetType();
            if (navPageType is not null && !Type.Equals(navPageType, preNavPageType))
            {
                if (navPageType == typeof(HomePage))
                {
                    frame.Content = Home;
                }
                if (navPageType == typeof(CreateInstancePage))
                {
                    frame.Content = CreateInstance;
                }
                if (navPageType == typeof(InstanceManagerPage))
                {
                    frame.Content = InstanceManager;
                }
                if (navPageType == typeof(ResDownloadPage))
                {
                    frame.Content = ResDownload;
                }
                if (navPageType == typeof(HelpPage))
                {
                    frame.Content = Help;
                }
                if (navPageType == typeof(SettingsPage))
                {
                    frame.Content = Settings;
                }
                if (navPageType == typeof(AboutPage))
                {
                    frame.Content = About;
                }
            }
        }
    }
}