using iNKORE.UI.WPF.Modern.Controls;
using iNKORE.UI.WPF.Modern.Media.Animation;
using MCServerLauncher.WPF.Console.View;
using System;
using System.Collections.Generic;
using System.Windows;
using Page = System.Windows.Controls.Page;

namespace MCServerLauncher.WPF.Console
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        private Page Board = new BoardPage();
        private Page Command = new CommandPage();
        private Page FileManager = new FileManagerPage();
        public MainWindow()
        {
            InitializeComponent();
            CurrentPage.Navigate(Board);
        }

        private void NavigationTriggered(NavigationView sender, NavigationViewItemInvokedEventArgs args)
        {
            if (args.IsSettingsInvoked)
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
            if (navPageType is not null && navPageType != preNavPageType)
            {
                switch (navPageType)
                {
                    case Type t when t == typeof(BoardPage):
                        CurrentPage.Navigate(Board, new SlideNavigationTransitionInfo { Effect = DetermineSlideDirection(t) });
                        break;
                    case Type t when t == typeof(CommandPage):
                        CurrentPage.Navigate(Command, new SlideNavigationTransitionInfo { Effect = DetermineSlideDirection(t) });
                        break;
                    case Type t when t == typeof(FileManagerPage):
                        CurrentPage.Navigate(FileManager, new SlideNavigationTransitionInfo { Effect = DetermineSlideDirection(t) });
                        break;
                }
            }
        }
        private SlideNavigationTransitionEffect DetermineSlideDirection(Type navPageType)
        {
            Page current = (Page)CurrentPage.Content;
            List<Type> pages = new List<Type> { typeof(BoardPage), typeof(CommandPage), typeof(FileManagerPage) };
            if (pages.IndexOf(current.GetType()) < pages.IndexOf(navPageType))
            {
                return SlideNavigationTransitionEffect.FromRight;
            }
            return SlideNavigationTransitionEffect.FromLeft;
        }
    }
}
