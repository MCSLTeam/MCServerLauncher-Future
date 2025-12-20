using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.DaemonClient;
using MCServerLauncher.DaemonClient.Connection;
using MCServerLauncher.WPF.Modules;
using MCServerLauncher.WPF.View.Components.Generic;
using MCServerLauncher.WPF.View.Components.InstanceManager;
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Animation;

namespace MCServerLauncher.WPF.View.Pages
{
    /// <summary>
    ///    InstanceManagerPage.xaml 的交互逻辑
    /// </summary>
    public partial class InstanceManagerPage : INotifyPropertyChanged
    {
        private readonly List<InstanceCard> _allInstanceCards = new();

        public InstanceManagerPage()
        {
            InitializeComponent();
            DataContext = this;
            LoadDaemonFilterItems();
            RunningStatusFilter.SelectionChanged += RunningStatusFilterChanged;
            IsVisibleChanged += (s, e) =>
            {
                if (IsVisible) RefreshButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            };
        }

        private ObservableCollection<string> _daemonFilterItems;
        public ObservableCollection<string> DaemonFilterItems
        {
            get => _daemonFilterItems;
            set
            {
                if (_daemonFilterItems != value)
                {
                    _daemonFilterItems = value;
                    OnPropertyChanged();
                }
            }
        }

        private void LoadDaemonFilterItems()
        {
            DaemonFilter.IsEnabled = false;
            DaemonFilterItems = new ObservableCollection<string>
            {
                Lang.Tr["AllDaemon"]
            };
#pragma warning disable CS8602 // 解引用可能出现空引用。
            if (DaemonsListManager.Get?.Count > 0)
            {
                var daemonDisplayNames = DaemonsListManager.Get
                    .Select(daemon => $"{daemon.FriendlyName} [{(daemon.IsSecure ? "wss" : "ws")}://{daemon.EndPoint}:{daemon.Port}]");
                foreach (var displayText in daemonDisplayNames)
                {
                    DaemonFilterItems.Add(displayText);
                }
            }
            
            DaemonFilter.SelectedIndex = 0;
            DaemonFilter.IsEnabled = true;
#pragma warning restore CS8602 // 解引用可能出现空引用。
        }
        
        private async void DaemonFilterChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is not ComboBox comboBox) return;
            await Refresh();
        }

        private async void RunningStatusFilterChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyFilters();
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await Refresh();
        }

        private async Task Refresh()
        {
            RefreshButton.IsEnabled = false;
            DaemonFilter.IsEnabled = false;
            RunningStatusFilter.IsEnabled = false;
            _allInstanceCards.Clear();
            InstanceCardGrid.Items.Clear();
            try
            {
                ShowLoadingLayer();
                if (DaemonFilter.SelectedIndex == 0)
                {
                    await LoadAllDaemonsInstances();
                }
                else if (DaemonFilter.SelectedIndex > 0)
                {
                    await LoadSingleDaemonInstances(DaemonFilter.SelectedIndex - 1);
                }
                
                ApplyFilters();
            }
            catch (Exception ex)
            {
                Log.Error($"[InstanceManager] Error loading instances: {ex.Message}");
            }
            finally
            {
                RefreshButton.IsEnabled = true;
                DaemonFilter.IsEnabled = true;
                RunningStatusFilter.IsEnabled = true;
            }
        }

        private async Task LoadAllDaemonsInstances()
        {
#pragma warning disable CS8602 // 解引用可能出现空引用。
            if (DaemonsListManager.Get == null || DaemonsListManager.Get.Count == 0)
                return;

            var loadTasks = new List<Task>();
            foreach (var daemonConfig in DaemonsListManager.Get)
            {
                loadTasks.Add(LoadDaemonInstancesAsync(daemonConfig));
            }
            
            await Task.WhenAll(loadTasks);
            HideLoadingLayer();
#pragma warning restore CS8602 // 解引用可能出现空引用。
        }

        private async Task LoadSingleDaemonInstances(int daemonIndex)
        {
#pragma warning disable CS8602 // 解引用可能出现空引用。
            if (DaemonsListManager.Get == null || daemonIndex >= DaemonsListManager.Get.Count)
                return;

            var daemonConfig = DaemonsListManager.Get[daemonIndex];
            await LoadDaemonInstancesAsync(daemonConfig);
            HideLoadingLayer();
#pragma warning restore CS8602 // 解引用可能出现空引用。
        }

        private async Task LoadDaemonInstancesAsync(Constants.DaemonConfigModel daemonConfig)
        {
            try {
            var daemon = await DaemonsWsManager.Get(daemonConfig);
            var instanceReports = await GetAllInstances(daemon);
                await AddInstanceCards(instanceReports);
            }
            catch (Exception ex)
            {
                Log.Error($"[InstanceManager] Failed to load instances from daemon {daemonConfig.EndPoint}:{daemonConfig.Port}: {ex.Message}");
            }
        }

        private async Task<Dictionary<Guid, InstanceReport>> GetAllInstances(IDaemon daemon)
        {
            return await DaemonExtensions.GetAllReportsAsync(daemon);
        }

        private async Task AddInstanceCards(Dictionary<Guid, InstanceReport> instanceReports)
        {
            if (instanceReports == null || instanceReports.Count == 0)
                return;

            foreach (var kvp in instanceReports)
            {
                Guid instanceId = kvp.Key;
                InstanceReport report = kvp.Value;

                var instanceCard = new InstanceCard
                {
                    InstanceId = instanceId,
                    InstanceName = report.Config.Name,
                    InstanceType = report.Config.InstanceType.ToString(),
                    McVersion = report.Config.McVersion ?? "",
                    Status = report.Status,
                    PlayerCount = report.Players?.Length ?? 0,
                    CpuUsage = report.PerformanceCounter.Cpu,
                    MemoryUsage = report.PerformanceCounter.Memory
                };

                _allInstanceCards.Add(instanceCard);
            }
        }

        private void ApplyFilters()
        {
            InstanceCardGrid.Items.Clear();

            var filteredCards = _allInstanceCards.AsEnumerable();

            // Apply running status filter
            if (RunningStatusFilter.SelectedItem is ComboBoxItem selectedItem)
            {
                string filterTag = selectedItem.Tag?.ToString() ?? "All";
                
                filteredCards = filterTag switch
                {
                    "Starting" => filteredCards.Where(card => card.Status == InstanceStatus.Starting),
                    "Running" => filteredCards.Where(card => card.Status == InstanceStatus.Running),
                    "Stopping" => filteredCards.Where(card => card.Status == InstanceStatus.Stopping),
                    "Stopped" => filteredCards.Where(card => card.Status == InstanceStatus.Stopped),
                    "Crashed" => filteredCards.Where(card => card.Status == InstanceStatus.Crashed),
                    _ => filteredCards // "All" - show everything
                };
            }

            // Add filtered cards to UI
            foreach (var card in filteredCards)
            {
                InstanceCardGrid.Items.Add(card);
            }
        }

        public void ShowLoadingLayer()
        {
            LoadingLayer.Visibility = Visibility.Visible;
            var fadeInAnimation = new DoubleAnimation
            {
                From = 0.0,
                To = 1.0,
                Duration = new Duration(TimeSpan.FromSeconds(0.4)),
                FillBehavior = FillBehavior.HoldEnd
            };
            LoadingLayer.BeginAnimation(OpacityProperty, fadeInAnimation);
        }

        public void HideLoadingLayer()
        {
            var fadeOutAnimation = new DoubleAnimation
            {
                From = 1.0,
                To = 0.0,
                Duration = new Duration(TimeSpan.FromSeconds(0.4)),
                FillBehavior = FillBehavior.HoldEnd
            };
            fadeOutAnimation.Completed += (s, e) =>
            {
                LoadingLayer.Visibility = Visibility.Hidden;
            };
            LoadingLayer.BeginAnimation(OpacityProperty, fadeOutAnimation);
        }
        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}