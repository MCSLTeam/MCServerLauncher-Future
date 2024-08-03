using iNKORE.UI.WPF.Modern.Controls;
using iNKORE.UI.WPF.Modern.Media.Animation;
using System.Collections.Generic;
using System;
using System.Windows.Controls;
using Page = System.Windows.Controls.Page;
using System.Windows;
using MCServerLauncher.WPF.Helpers;

namespace MCServerLauncher.WPF.View.FirstSetupHelper
{
    /// <summary>
    ///     FirstSetup.xaml 的交互逻辑
    /// </summary>
    public partial class FirstSetup
    {
        private readonly Page _eula = new EulaSetupPage();
        private readonly Page _daemon = new DaemonSetupPage();
        private readonly Page _welcome = new WelcomeSetupPage();

        private readonly List<Type> _pageList = new()
        {
            typeof(EulaSetupPage),
            typeof(DaemonSetupPage),
            typeof(WelcomeSetupPage)
        };
        public FirstSetup()
        {
            InitializeComponent();
            CurrentPage.Navigate(_eula);
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
                case not null when navPageType == typeof(EulaSetupPage):
                    CurrentPage.Navigate(_eula,
                        new SlideNavigationTransitionInfo { Effect = DetermineSlideDirection(navPageType) });
                    break;
                case not null when navPageType == typeof(DaemonSetupPage):
                    CurrentPage.Navigate(_daemon,
                        new SlideNavigationTransitionInfo { Effect = DetermineSlideDirection(navPageType) });
                    break;
                case not null when navPageType == typeof(WelcomeSetupPage):
                    CurrentPage.Navigate(_welcome,
                        new SlideNavigationTransitionInfo { Effect = DetermineSlideDirection(navPageType) });
                    break;
            }
        }

        private SlideNavigationTransitionEffect DetermineSlideDirection(Type navPageType)
        {
            var current = (Page)CurrentPage.Content;
            return _pageList.IndexOf(current.GetType()) < _pageList.IndexOf(navPageType) ? SlideNavigationTransitionEffect.FromRight : SlideNavigationTransitionEffect.FromLeft;
        }
        public void FinishSetup()
        {
            Visibility = Visibility.Hidden;
            var parent = this.TryFindParent<MainWindow>();
            parent.NavView.Visibility = Visibility.Visible;
        }
        public void GoDaemonSetup()
        {
            RefreshNavMenu(newIdx: 1);
        }
        public void GoWelcomeSetup()
        {
            RefreshNavMenu(newIdx: 2);
        }

        private void RefreshNavMenu(int newIdx)
        {
            NavView.SelectedItem = NavView.MenuItems[newIdx];
            foreach (NavigationViewItemBase item in NavView.MenuItems)
            {
                if (NavView.MenuItems.IndexOf(item) == newIdx)
                {
                    NavView.SelectedItem = NavView.MenuItems[NavView.MenuItems.IndexOf(item)];
                    item.IsEnabled = true;
                }
                else
                {
                    item.IsEnabled = false;
                }
            }
            NavigateTo(_pageList[newIdx], new SlideNavigationTransitionInfo { Effect = DetermineSlideDirection(_pageList[newIdx]) });
        }
    }
}
