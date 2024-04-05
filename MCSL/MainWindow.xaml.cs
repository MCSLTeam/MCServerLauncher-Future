
using iNKORE.UI.WPF.Modern.Controls;
using System.Windows;
using iNKORE.UI.WPF.Modern.Media.Animation;
using iNKORE.UI.WPF.Modern;
using System;
using MCServerLauncher.Pages;

namespace MCServerLauncher
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {

        private System.Windows.Controls.Page Home = new HomePage();
        private System.Windows.Controls.Page CreateInstance = new CreateInstancePage();
        private System.Windows.Controls.Page InstanceManager = new InstanceManagerPage();
        private System.Windows.Controls.Page ResDownload = new ResDownloadPage();

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
            }
        }
    }
}