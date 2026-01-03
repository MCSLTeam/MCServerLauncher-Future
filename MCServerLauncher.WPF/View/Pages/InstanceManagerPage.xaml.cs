using iNKORE.UI.WPF.Modern.Common.IconKeys;
using iNKORE.UI.WPF.Modern.Controls;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.DaemonClient;
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
        private int _selectedCount = 0;

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
            ContainerEmptyTip.ActionButton.Click += GoCreateInstance;
            ContainerEmptyTip.ActionButton.Content = new IconAndText() { Icon = SegoeFluentIcons.Add, Content = Lang.Tr["Main_CreateInstanceNavMenu"] };
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
            DaemonFilterItems = new ObservableCollection<string>();
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
            if (!(DaemonsListManager.Get.Count > 0))
            {

            }
            RefreshButton.IsEnabled = false;
            DaemonFilter.IsEnabled = false;
            RunningStatusFilter.IsEnabled = false;
            _allInstanceCards.Clear();
            InstanceCardGrid.Items.Clear();
            ShowLoadingLayer();
            await LoadDaemonInstances();
            ApplyFilters();
            RefreshButton.IsEnabled = true;
            DaemonFilter.IsEnabled = true;
            RunningStatusFilter.IsEnabled = true;
            }

        private async Task LoadDaemonInstances()
        {
#pragma warning disable CS8602 // 解引用可能出现空引用。
            var daemonIndex = DaemonFilter.SelectedIndex;
            Console.WriteLine(daemonIndex);
            if (!(DaemonsListManager.Get.Count > 0) || daemonIndex + 1 >= DaemonsListManager.Get.Count)
                return;

            var daemonConfig = DaemonsListManager.Get[daemonIndex];
            try
            {
                var daemon = await DaemonsWsManager.Get(daemonConfig);
                var instanceReports = await DaemonExtensions.GetAllReportsAsync(daemon);
                await AddInstanceCards(instanceReports);
            }
            catch (Exception ex)
            {
                ContainerEmptyTip.Visibility = Visibility.Visible;
                InstanceCardGrid.Visibility = Visibility.Collapsed;
                Log.Error($"[InstanceManager] Failed to load instances from daemon {daemonConfig.EndPoint}:{daemonConfig.Port}: {ex.Message}");
            }
            HideLoadingLayer();
#pragma warning restore CS8602 // 解引用可能出现空引用。
        }

        private async Task AddInstanceCards(Dictionary<Guid, InstanceReport> instanceReports)
        {
            if (instanceReports == null || instanceReports.Count == 0)
                return;

            // Get current daemon config
            var currentDaemonConfig = DaemonsListManager.Get?[DaemonFilter.SelectedIndex];

            foreach (var kvp in instanceReports)
            {
                Guid instanceId = kvp.Key;
                InstanceReport report = kvp.Value;

                var instanceCard = new InstanceCard
                {
                    InstanceId = instanceId,
                    daemonAddr = currentDaemonConfig.EndPoint,
                    DaemonConfig = currentDaemonConfig,
                    InstanceName = report.Config.Name,
                    InstanceType = report.Config.InstanceType.ToString(),
                    McVersion = report.Config.McVersion ?? "",
                    Status = report.Status,
                    PlayerCount = report.Players?.Length ?? 0,
                    CpuUsage = report.PerformanceCounter.Cpu,
                    MemoryUsage = report.PerformanceCounter.Memory
                };

                // Subscribe to selection change event
                instanceCard.SelectionChanged += InstanceCard_SelectionChanged;

                _allInstanceCards.Add(instanceCard);
            }
        }

        private void InstanceCard_SelectionChanged(object? sender, bool isSelected)
        {
            _selectedCount += isSelected ? 1 : -1;
            UpdateBatchOperationBar();
        }

        private void UpdateBatchOperationBar()
        {
            SelectedCountTextBlock.Text = $"{_selectedCount} selected";
            BatchOperationBar.Visibility = _selectedCount > 0 ? Visibility.Visible : Visibility.Collapsed;
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
            ContainerEmptyTip.Visibility = Visibility.Collapsed;
            InstanceCardGrid.Visibility = Visibility.Visible;
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

        private async void GoCreateInstance(object sender, RoutedEventArgs e)
        {
            VisualTreeHelper.Navigate("MCServerLauncher.WPF.View.Pages.CreateInstancePage", "_createInstance");
        }

        #region Batch Operations

        private void BatchSelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in InstanceCardGrid.Items)
            {
                if (item is InstanceCard card)
                {
                    card.IsSelected = true;
                }
            }
        }

        private void BatchCancel_Click(object sender, RoutedEventArgs e)
        {
            foreach (var card in _allInstanceCards)
            {
                card.IsSelected = false;
            }
            _selectedCount = 0;
            UpdateBatchOperationBar();
        }

        private async void BatchStart_Click(object sender, RoutedEventArgs e)
        {
            var selectedCards = _allInstanceCards.Where(c => c.IsSelected).ToList();
            if (selectedCards.Count == 0) return;

            var dialog = new ContentDialog
            {
                Title = "Confirm Batch Start",
                Content = $"Start {selectedCards.Count} instances?",
                PrimaryButtonText = "Start",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;

            int successCount = 0;
            int failCount = 0;

            foreach (var card in selectedCards)
            {
                try
                {
                    var daemon = await DaemonsWsManager.Get(card.DaemonConfig);
                    if (daemon != null)
                    {
                        await daemon.StartInstanceAsync(card.InstanceId);
                        successCount++;
                    }
                    else
                    {
                        failCount++;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[InstanceManagerPage] Failed to start instance {0}", card.InstanceId);
                    failCount++;
                }
            }

            Notification.Push(
                "Batch Start Complete",
                $"Success: {successCount}, Failed: {failCount}",
                false,
                failCount > 0 ? iNKORE.UI.WPF.Modern.Controls.InfoBarSeverity.Warning : iNKORE.UI.WPF.Modern.Controls.InfoBarSeverity.Success
            );

            BatchCancel_Click(sender, e);
            await Task.Delay(2000);
            RefreshButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        }

        private async void BatchStop_Click(object sender, RoutedEventArgs e)
        {
            var selectedCards = _allInstanceCards.Where(c => c.IsSelected).ToList();
            if (selectedCards.Count == 0) return;

            var dialog = new ContentDialog
            {
                Title = "Confirm Batch Stop",
                Content = $"Stop {selectedCards.Count} instances?",
                PrimaryButtonText = "Stop",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;

            int successCount = 0;
            int failCount = 0;

            foreach (var card in selectedCards)
            {
                try
                {
                    var daemon = await DaemonsWsManager.Get(card.DaemonConfig);
                    if (daemon != null)
                    {
                        await daemon.StopInstanceAsync(card.InstanceId);
                        successCount++;
                    }
                    else
                    {
                        failCount++;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[InstanceManagerPage] Failed to stop instance {0}", card.InstanceId);
                    failCount++;
                }
            }

            Notification.Push(
                "Batch Stop Complete",
                $"Success: {successCount}, Failed: {failCount}",
                false,
                failCount > 0 ? iNKORE.UI.WPF.Modern.Controls.InfoBarSeverity.Warning : iNKORE.UI.WPF.Modern.Controls.InfoBarSeverity.Success
            );

            BatchCancel_Click(sender, e);
            await Task.Delay(2000);
            RefreshButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        }

        private async void BatchDelete_Click(object sender, RoutedEventArgs e)
        {
            var selectedCards = _allInstanceCards.Where(c => c.IsSelected).ToList();
            if (selectedCards.Count == 0) return;

            var dialog = new ContentDialog
            {
                Title = "Confirm Batch Delete",
                Content = $"Delete {selectedCards.Count} instances?\nThis action CANNOT be undone!",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;

            int successCount = 0;
            int failCount = 0;

            foreach (var card in selectedCards)
            {
                try
                {
                    var daemon = await DaemonsWsManager.Get(card.DaemonConfig);
                    if (daemon != null)
                    {
                        await daemon.RemoveInstanceAsync(card.InstanceId);
                        successCount++;
                    }
                    else
                    {
                        failCount++;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[InstanceManagerPage] Failed to delete instance {0}", card.InstanceId);
                    failCount++;
                }
            }

            Notification.Push(
                "Batch Delete Complete",
                $"Success: {successCount}, Failed: {failCount}",
                false,
                failCount > 0 ? iNKORE.UI.WPF.Modern.Controls.InfoBarSeverity.Warning : iNKORE.UI.WPF.Modern.Controls.InfoBarSeverity.Success
            );

            BatchCancel_Click(sender, e);
            await Task.Delay(1000);
            RefreshButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        }

        #endregion
    }
}