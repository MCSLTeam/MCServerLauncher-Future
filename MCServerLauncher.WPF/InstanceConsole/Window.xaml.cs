using iNKORE.UI.WPF.Modern.Controls;
using iNKORE.UI.WPF.Modern.Media.Animation;
using MCServerLauncher.WPF.InstanceConsole.Modules;
using MCServerLauncher.WPF.InstanceConsole.View.Pages;
using MCServerLauncher.WPF.Modules;
using Serilog;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Page = System.Windows.Controls.Page;

namespace MCServerLauncher.WPF.InstanceConsole
{
    /// <summary>
    ///    Instance Console Window
    /// </summary>
    public partial class Window
    {
        private readonly Page _board = new BoardPage();
        private readonly Page _command = new CommandPage();
        private readonly Page _componentManager = new ComponentManagerPage();
        private readonly Page _eventTrigger = new EventTriggerPage();
        private readonly Page _fileManager = new FileManagerPage();

        private Constants.DaemonConfigModel? _daemonConfig;
        private Guid _instanceId;

        public Window()
        {
            InitializeComponent();
            Loaded += Window_Loaded;
            Closing += Window_Closing;
        }

        /// <summary>
        /// Initialize console window with daemon and instance information
        /// </summary>
        public void Initialize(Constants.DaemonConfigModel daemonConfig, Guid instanceId)
        {
            _daemonConfig = daemonConfig;
            _instanceId = instanceId;

            // Update window title
            Title = $"Instance Console - {instanceId}";
        }

        private async void Window_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            if (_daemonConfig == null || _instanceId == Guid.Empty)
            {
                Log.Error("[InstanceConsole] Window not properly initialized");
                Notification.Push(
                    "Error",
                    "Instance console not properly initialized",
                    true,
                    iNKORE.UI.WPF.Modern.Controls.InfoBarSeverity.Error
                );
                return;
            }

            try
            {
                // Initialize data manager
                await InstanceDataManager.Instance.InitializeAsync(_daemonConfig, _instanceId);

                // Navigate to board page
                CurrentPage.Navigate(_board);

                Log.Information("[InstanceConsole] Window loaded successfully for instance {0}", _instanceId);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[InstanceConsole] Failed to load window");
                Notification.Push(
                    "Error",
                    $"Failed to load instance console: {ex.Message}",
                    true,
                    iNKORE.UI.WPF.Modern.Controls.InfoBarSeverity.Error
                );
            }
        }

        private async void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                await InstanceDataManager.Instance.DisposeAsync();
                Log.Information("[InstanceConsole] Window closed");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[InstanceConsole] Error during window close");
            }
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