
using iNKORE.UI.WPF.Modern.Controls;
using System.Windows;
using iNKORE.UI.WPF.Modern.Media.Animation;
using iNKORE.UI.WPF.Modern;
using System;
using MCServerLauncher.Pages;
using Page = System.Windows.Controls.Page;
using MessageBox = iNKORE.UI.WPF.Modern.Controls.MessageBox;

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
            //默认主题
            ThemeManager.Current.ApplicationTheme = ApplicationTheme.Dark;

            //默认页面
            frame.Content = Home;
        }

        //导航栏
        private void NavigationView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
        {
            if (args.IsSettingsInvoked == true)
            {
                NavigationView_Navigate(typeof(int), args.RecommendedNavigationTransitionInfo);
            }
            else if (args.InvokedItemContainer != null)
            {
                NavigationView_Navigate(Type.GetType(args.InvokedItemContainer.Tag.ToString()), args.RecommendedNavigationTransitionInfo);
            }
        }

        private void NavigationView_Navigate(Type navPageType, NavigationTransitionInfo transitionInfo)
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