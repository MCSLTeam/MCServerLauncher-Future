using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.WPF.InstanceConsole.Modules;
using Serilog;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace MCServerLauncher.WPF.InstanceConsole.View.Pages
{
    /// <summary>
    ///    BoardPage.xaml 的交互逻辑
    /// </summary>
    public partial class BoardPage : Page
    {
        public BoardPage()
        {
            InitializeComponent();
            Loaded += async (s, e) => await InitializeComponentsAsync();
            
            // Subscribe to report updates to handle instance type changes
            InstanceDataManager.Instance.ReportUpdated += OnReportUpdated;
        }

        private void OnReportUpdated(object? sender, InstanceReport? report)
        {
            Dispatcher.Invoke(() => UpdateComponentsVisibility());
        }

        private void UpdateComponentsVisibility()
        {
            var report = InstanceDataManager.Instance.CurrentReport;
            if (report == null)
                return;

            var isMinecraftInstance = report.Config.InstanceType.SupportsMinecraftBoardWidgets();

            // Show/hide Minecraft-specific components
            AddressComponent.Visibility = isMinecraftInstance ? Visibility.Visible : Visibility.Collapsed;
            PlayerListComponent.Visibility = isMinecraftInstance ? Visibility.Visible : Visibility.Collapsed;

            // Universal components (PerformanceComponent, ConnectionInfoComponent) are always visible
        }

        public async Task DisposeAsync()
        {
            InstanceDataManager.Instance.ReportUpdated -= OnReportUpdated;
            
            await Task.WhenAll(
                PerformanceComponent.DisposeAsync(),
                ConnectionInfoComponent.DisposeAsync(),
                AddressComponent.DisposeAsync(),
                PlayerListComponent.DisposeAsync()
            );
        }

        private async Task InitializeComponentsAsync()
        {
            try
            {
                // Initialize all board components
                await Task.WhenAll(
                    PerformanceComponent.InitializeAsync(),
                    ConnectionInfoComponent.InitializeAsync(),
                    AddressComponent.InitializeAsync(),
                    PlayerListComponent.InitializeAsync()
                );

                // Update visibility after initialization
                UpdateComponentsVisibility();

                Log.Information("[BoardPage] All components initialized");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[BoardPage] Failed to initialize components");
            }
        }
    }
}