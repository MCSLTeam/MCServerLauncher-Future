using iNKORE.UI.WPF.Modern.Controls;
using iNKORE.UI.WPF.Modern.Media.Animation;
using MCServerLauncher.WPF.Console.View;
using System;
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
        public MainWindow()
        {
            InitializeComponent();
            CurrentPage.Navigate(Board);
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
                if (navPageType == typeof(BoardPage))
                {
                    CurrentPage.Navigate(Board);
                }
            }
        }
    }
}
