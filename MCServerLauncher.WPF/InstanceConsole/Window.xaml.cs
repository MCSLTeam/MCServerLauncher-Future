using iNKORE.UI.WPF.Modern.Controls;
using iNKORE.UI.WPF.Modern.Media.Animation;
using MCServerLauncher.WPF.InstanceConsole.View.Pages;
using System;
using System.Collections.Generic;
using Page = System.Windows.Controls.Page;

namespace MCServerLauncher.WPF.InstanceConsole
{
    /// <summary>
    ///    Window.xaml 的交互逻辑
    /// </summary>
    public partial class Window
    {
        private readonly Page _board = new BoardPage();
        private readonly Page _command = new CommandPage();
        private readonly Page _componentManager = new ComponentManagerPage();
        private readonly Page _eventTrigger = new EventTriggerPage();
        private readonly Page _fileManager = new FileManagerPage();

        public Window()
        {
            InitializeComponent();
            CurrentPage.Navigate(_board);
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
                case not null when navPageType == typeof(BoardPage):
                    CurrentPage.Navigate(_board,
                        new SlideNavigationTransitionInfo { Effect = DetermineSlideDirection(navPageType) });
                    break;
                case not null when navPageType == typeof(CommandPage):
                    CurrentPage.Navigate(_command,
                        new SlideNavigationTransitionInfo { Effect = DetermineSlideDirection(navPageType) });
                    break;
                case not null when navPageType == typeof(FileManagerPage):
                    CurrentPage.Navigate(_fileManager,
                        new SlideNavigationTransitionInfo { Effect = DetermineSlideDirection(navPageType) });
                    break;
                case not null when navPageType == typeof(EventTriggerPage):
                    CurrentPage.Navigate(_eventTrigger,
                        new SlideNavigationTransitionInfo { Effect = DetermineSlideDirection(navPageType) });
                    break;
                case not null when navPageType == typeof(ComponentManagerPage):
                    CurrentPage.Navigate(_componentManager,
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
            var pages = new List<Type>
            {
                typeof(BoardPage), typeof(CommandPage), typeof(FileManagerPage), typeof(EventTriggerPage),
                typeof(ComponentManagerPage)
            };
            return pages.IndexOf(current.GetType()) < pages.IndexOf(navPageType)
                ? SlideNavigationTransitionEffect.FromRight
                : SlideNavigationTransitionEffect.FromLeft;
        }
    }
}