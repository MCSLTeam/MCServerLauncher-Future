using iNKORE.UI.WPF.Modern.Controls;
using iNKORE.UI.WPF.Modern.Media.Animation;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media.Animation;
using static MCServerLauncher.WPF.Modules.VisualTreeHelper;
using static MCServerLauncher.WPF.Modules.Animation;
using Page = System.Windows.Controls.Page;
using MCServerLauncher.WPF.Modules;

namespace MCServerLauncher.WPF.View.FirstSetupHelper
{
    /// <summary>
    ///    FirstSetup.xaml 的交互逻辑
    /// </summary>
    public partial class FirstSetup
    {
        private readonly Page _language = new LanguageSetupPage();
        private readonly Page _eula = new EulaSetupPage();
        private readonly Page _daemon = new DaemonSetupPage();
        private readonly Page _welcome = new WelcomeSetupPage();

        private readonly List<Type> _pageList = new()
        {
            typeof(LanguageSetupPage),
            typeof(EulaSetupPage),
            typeof(DaemonSetupPage),
            typeof(WelcomeSetupPage)
        };

        public FirstSetup()
        {
            InitializeComponent();
            CurrentPage.Navigate(_language);
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
                case not null when navPageType == typeof(LanguageSetupPage):
                    CurrentPage.Navigate(_language,
                        new SlideNavigationTransitionInfo { Effect = DetermineSlideDirection(navPageType) });
                    break;
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


        /// <summary>
        ///    Determine the transition animation.
        /// </summary>
        /// <param name="navPageType">Type of the page.</param>
        /// <returns>Transition info.</returns>
        private SlideNavigationTransitionEffect DetermineSlideDirection(Type navPageType)
        {
            var current = (Page)CurrentPage.Content;
            return _pageList.IndexOf(current.GetType()) < _pageList.IndexOf(navPageType)
                ? SlideNavigationTransitionEffect.FromRight
                : SlideNavigationTransitionEffect.FromLeft;
        }
        public void FinishSetup()
        {
            var fadeOutAnimation = FadeOutAnimation();
            fadeOutAnimation.Completed += (s, e) =>
            {
                Visibility = Visibility.Hidden;
            };

            var parent = this.TryFindParent<MainWindow>();
            var fadeInAnimation = FadeInAnimation();
            fadeInAnimation.Completed += (s, e) =>
            {
                if (parent == null) return;
                parent.NavView.Visibility = Visibility.Visible;
                parent.TitleBarRootBorder.Visibility = Visibility.Visible;
            };
            BeginAnimation(OpacityProperty, fadeOutAnimation);
            parent?.NavView.BeginAnimation(OpacityProperty, fadeInAnimation);
        }
        public void GoEulaSetup()
        {
            RefreshNavMenu(1);
        }
        public void GoDaemonSetup()
        {
            RefreshNavMenu(2);
        }

        public void GoWelcomeSetup()
        {
            RefreshNavMenu(3);
        }

        /// <summary>
        ///    Refresh the navigation menu.
        /// </summary>
        /// <param name="newIdx"></param>
        private void RefreshNavMenu(int newIdx)
        {
            NavView.SelectedItem = NavView.MenuItems[newIdx];
            foreach (NavigationViewItemBase item in NavView.MenuItems)
                if (NavView.MenuItems.IndexOf(item) == newIdx)
                {
                    NavView.SelectedItem = NavView.MenuItems[NavView.MenuItems.IndexOf(item)];
                    item.IsEnabled = true;
                }
                else
                {
                    item.IsEnabled = false;
                }

            NavigateTo(_pageList[newIdx],
                new SlideNavigationTransitionInfo { Effect = DetermineSlideDirection(_pageList[newIdx]) });
        }
    }
}