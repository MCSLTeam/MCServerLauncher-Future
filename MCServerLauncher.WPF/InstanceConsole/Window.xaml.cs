using iNKORE.UI.WPF.Modern.Controls;
using iNKORE.UI.WPF.Modern.Media.Animation;
using MCServerLauncher.WPF.InstanceConsole.Modules;
using MCServerLauncher.WPF.InstanceConsole.ViewModels;
using MCServerLauncher.WPF.InstanceConsole.View.Pages;
using MCServerLauncher.WPF.Modules;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
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
        private readonly Page _instanceSettings = new InstanceSettingsPage();

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

            UpdateWindowTitle();
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
                    InfoBarSeverity.Error
                );
                return;
            }

            try
            {
                // Initialize data manager
                await InstanceDataManager.Instance.InitializeAsync(_daemonConfig, _instanceId);
                InstanceDataManager.Instance.ReportUpdated += OnInstanceReportUpdated;
                UpdateWindowTitle();

                // Navigate to board page
                CurrentPage.Navigate(_board);
                await WarnAboutClientSideModsAsync();

                Log.Information("[InstanceConsole] Window loaded successfully for instance {0}", _instanceId);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[InstanceConsole] Failed to load window");
                Notification.Push(
                    "Error",
                    $"Failed to load instance console: {ex.Message}",
                    true,
                    InfoBarSeverity.Error
                );
            }
        }

        private void OnInstanceReportUpdated(object? sender, Common.ProtoType.Instance.InstanceReport? e)
        {
            Dispatcher.Invoke(UpdateWindowTitle);
        }

        private void UpdateWindowTitle()
        {
            var instanceName = GetInstanceTitleName();
            var nodeName = GetNodeTitleName();
            Title = BuildSystemWindowTitle(instanceName, nodeName);
            InstanceTitleText.Text = instanceName;
            NodeTitleText.Text = nodeName;
        }

        private string GetInstanceTitleName()
        {
            var instanceName = InstanceDataManager.Instance.CurrentReport?.Config.Name;
            if (string.IsNullOrWhiteSpace(instanceName))
            {
                instanceName = _instanceId == Guid.Empty ? string.Empty : _instanceId.ToString();
            }

            return instanceName;
        }

        private string GetNodeTitleName()
        {
            var daemonHost = _daemonConfig?.FriendlyName;
            if (string.IsNullOrWhiteSpace(daemonHost))
            {
                daemonHost = Lang.Tr["Main_DaemonManagerNavMenu"];
            }

            return daemonHost;
        }

        private static string BuildSystemWindowTitle(string instanceName, string nodeName)
        {
            var instanceText = string.Format(Lang.Tr["InstanceConsole_InstanceTitlePart"], instanceName);
            var nodeText = string.Format(Lang.Tr["InstanceConsole_NodeTitlePart"], nodeName);
            return $"{Lang.Tr["ConsoleTitle"]} - {instanceText} - {nodeText}";
        }

        private async Task WarnAboutClientSideModsAsync()
        {
            var daemon = InstanceDataManager.Instance.CurrentDaemon;
            if (daemon == null) return;

            try
            {
                var scanResult = await ComponentScanner.ScanAsync(daemon, _instanceId);
                var clientSideMods = scanResult.Mods
                    .Where(item => item.IsEnabled && item.IsClientSideOnly)
                    .ToArray();
                if (clientSideMods.Length == 0) return;

                var listView = new iNKORE.UI.WPF.Modern.Controls.ListView
                {
                    ItemsSource = clientSideMods.Select(item => item.Title),
                    MaxHeight = 240,
                    Margin = new System.Windows.Thickness(0, 12, 0, 0)
                };

                var panel = new System.Windows.Controls.StackPanel();
                panel.Children.Add(new System.Windows.Controls.TextBlock
                {
                    Text = string.Format(Lang.Tr["ComponentManager_ClientSideModsWarning"], clientSideMods.Length),
                    TextWrapping = System.Windows.TextWrapping.Wrap
                });
                panel.Children.Add(listView);

                var dialog = new ContentDialog
                {
                    Title = Lang.Tr["ComponentManager_ClientSideModsFound"],
                    Content = panel,
                    PrimaryButtonText = Lang.Tr["ComponentManager_DisableClientSideMods"],
                    SecondaryButtonText = Lang.Tr["Ignore"],
                    DefaultButton = ContentDialogButton.Primary
                };

                var result = await dialog.ShowAsync();
                if (result != ContentDialogResult.Primary) return;

                foreach (var item in clientSideMods)
                {
                    await ComponentScanner.DisableAsync(daemon, item);
                }

                Notification.Push(
                    Lang.Tr["Success"],
                    string.Format(Lang.Tr["ComponentManager_DisabledClientSideMods"], clientSideMods.Length),
                    false,
                    InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[InstanceConsole] Failed to scan client-side mods");
            }
        }

        private async void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                if (_board is BoardPage boardPage)
                {
                    await boardPage.DisposeAsync();
                }
                if (_command is CommandPage commandPage)
                {
                    await commandPage.DisposeAsync();
                }

                InstanceDataManager.Instance.ReportUpdated -= OnInstanceReportUpdated;
                
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
            {
                var navPageType = Type.GetType(args.InvokedItemContainer.Tag?.ToString() ?? string.Empty);
                if (navPageType is not null)
                    NavigateTo(navPageType, args.RecommendedNavigationTransitionInfo);
            }
        }

        /// <summary>
        ///    Navigation to a specific page.
        /// </summary>
        /// <param name="navPageType">Type of the page.</param>
        /// <param name="transitionInfo">Transition animation.</param>
#pragma warning disable IDE0060 // 删除未使用的参数
        private void NavigateTo(Type navPageType, NavigationTransitionInfo _transitionInfo)
#pragma warning restore IDE0060 // 删除未使用的参数
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
                case not null when navPageType == typeof(InstanceSettingsPage):
                    CurrentPage.Navigate(_instanceSettings,
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
                typeof(ComponentManagerPage), typeof(InstanceSettingsPage)
            };
            return pages.IndexOf(current.GetType()) < pages.IndexOf(navPageType)
                ? SlideNavigationTransitionEffect.FromRight
                : SlideNavigationTransitionEffect.FromLeft;
        }
    }
}
