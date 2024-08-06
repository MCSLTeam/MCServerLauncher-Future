using System;
using System.Collections.Generic;
using System.Windows;
using iNKORE.UI.WPF.Modern.Controls;
using iNKORE.UI.WPF.Modern.Media.Animation;
using MCServerLauncher.WPF.Console.View;
using Page = System.Windows.Controls.Page;

namespace MCServerLauncher.WPF.Console
{
    /// <summary>
    ///     MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly Page _board = new BoardPage();
        private readonly Page _command = new CommandPage();
        private readonly Page _componentManager = new ComponentManagerPage();
        private readonly Page _eventTrigger = new EventTriggerPage();
        private readonly Page _fileManager = new FileManagerPage();

        public MainWindow()
        {
            InitializeComponent();
            CurrentPage.Navigate(_board);
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

        private SlideNavigationTransitionEffect DetermineSlideDirection(Type navPageType)
        {
            var current = (Page)CurrentPage.Content;
            var pages = new List<Type>
            {
                typeof(BoardPage), typeof(CommandPage), typeof(FileManagerPage), typeof(EventTriggerPage),
                typeof(ComponentManagerPage)
            };
            return pages.IndexOf(current.GetType()) < pages.IndexOf(navPageType) ? SlideNavigationTransitionEffect.FromRight : SlideNavigationTransitionEffect.FromLeft;
        }
    }
}