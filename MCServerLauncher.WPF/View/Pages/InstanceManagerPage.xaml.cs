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
        private readonly List<InstanceCard> _allInstanceCards = [];
        private int _selectedCount = 0;
        private System.Windows.Threading.DispatcherTimer? _refreshTimer;

        public InstanceManagerPage()
        {
            InitializeComponent();
            DataContext = this;
            LoadDaemonFilterItems();
            RunningStatusFilter.SelectionChanged += RunningStatusFilterChanged;
            IsVisibleChanged += (s, e) =>
            {
                if (IsVisible)
                {
                    RefreshButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                    StartAutoRefresh();
                }
                else
                {
                    StopAutoRefresh();
                }
            };
        }

        private void StartAutoRefresh()
        {
            var interval = SettingsManager.Get.Instance.AutoRefreshInterval;
            if (interval <= 0)
            {
                StopAutoRefresh();
                return;
            }

            if (_refreshTimer == null)
            {
                _refreshTimer = new System.Windows.Threading.DispatcherTimer();
                _refreshTimer.Tick += async (s, e) => await Refresh(true);
            }
            _refreshTimer.Interval = TimeSpan.FromSeconds(interval);
            _refreshTimer.Start();
        }

        private void StopAutoRefresh()
        {
            _refreshTimer?.Stop();
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
            DaemonFilterItems = [];
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
        }
        
        private async void DaemonFilterChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is not ComboBox) return;
            await Refresh();
        }

        private async void RunningStatusFilterChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyFilters();
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await Refresh(false);
        }

        private async Task Refresh(bool isAutoRefresh = false)
        {
            if (!isAutoRefresh)
            {
                _allInstanceCards.Clear();
                InstanceCardGrid.Items.Clear();
            }
            
            if (!(DaemonsListManager.Get!.Count > 0))
            {
                if (!isAutoRefresh) ShowNoDaemonLayer();
                return;
            }

            if (!isAutoRefresh)
            {
                RefreshButton.IsEnabled = false;
                DaemonFilter.IsEnabled = false;
                RunningStatusFilter.IsEnabled = false;
                ShowLoadingLayer();
            }

            await LoadDaemonInstances(isAutoRefresh);
            ApplyFilters(isAutoRefresh);

            if (!isAutoRefresh)
            {
                RefreshButton.IsEnabled = true;
                DaemonFilter.IsEnabled = true;
                RunningStatusFilter.IsEnabled = true;
            }
        }

        private async Task LoadDaemonInstances(bool isAutoRefresh = false)
        {
            if (!isAutoRefresh) StopTipLayer.Visibility = Visibility.Collapsed;
            var daemonIndex = DaemonFilter.SelectedIndex;
            if (!(DaemonsListManager.Get!.Count > 0))
            {
                if (!isAutoRefresh)
                {
                    HideLoadingLayer();
                    ShowNoDaemonLayer();
                }
                return;
            }

            var daemonConfig = DaemonsListManager.Get[daemonIndex];
            try
            {
                var daemon = await DaemonsWsManager.Get(daemonConfig) ?? throw new Exception("Daemon is offline or unreachable.");
                var instanceReports = await DaemonExtensions.GetAllReportsAsync(daemon);

                await AddInstanceCards(instanceReports, isAutoRefresh);
            }
            catch (Exception ex)
            {
                if (!isAutoRefresh)
                {
                    InstanceCardGrid.Visibility = Visibility.Collapsed;
                    ShowLoadErrorLayer();
                }
                Log.Error($"[InstanceManager] Failed to load instances from daemon {daemonConfig.EndPoint}:{daemonConfig.Port}: {ex.Message}");
            }
            if (!isAutoRefresh) HideLoadingLayer();
        }

        private async Task AddInstanceCards(Dictionary<Guid, InstanceReport> instanceReports, bool isAutoRefresh = false)
        {
            if (instanceReports == null || instanceReports.Count == 0)
            {
                if (!isAutoRefresh) ShowNoInstanceLayer();
                return;
            }

            // Get current daemon config
            var currentDaemonConfig = DaemonsListManager.Get?[DaemonFilter.SelectedIndex];

            if (isAutoRefresh)
            {
                // Update existing cards or add new ones
                var existingIds = _allInstanceCards.Select(c => c.InstanceId).ToList();
                var newIds = instanceReports.Keys.ToList();

                // Remove deleted instances
                var idsToRemove = existingIds.Except(newIds).ToList();
                foreach (var id in idsToRemove)
                {
                    var cardToRemove = _allInstanceCards.FirstOrDefault(c => c.InstanceId == id);
                    if (cardToRemove != null)
                    {
                        _allInstanceCards.Remove(cardToRemove);
                    }
                }

                // Update or add instances
                foreach (var kvp in instanceReports)
                {
                    Guid instanceId = kvp.Key;
                    InstanceReport report = kvp.Value;

                    var existingCard = _allInstanceCards.FirstOrDefault(c => c.InstanceId == instanceId);
                    if (existingCard != null)
                    {
                        // Update existing card properties
                        existingCard.Status = report.Status;
                        existingCard.CpuUsage = report.PerformanceCounter.Cpu;
                        existingCard.MemoryUsage = report.PerformanceCounter.Memory;
                    }
                    else
                    {
                        // Add new card
                        var instanceCard = new InstanceCard
                        {
                            InstanceId = instanceId,
                            daemonAddr = currentDaemonConfig!.EndPoint!,
                            DaemonConfig = currentDaemonConfig,
                            InstanceName = report.Config.Name,
                            InstanceType = report.Config.InstanceType.ToString(),
                            Version = report.Config.Version ?? "",
                            Status = report.Status,
                            CpuUsage = report.PerformanceCounter.Cpu,
                            MemoryUsage = report.PerformanceCounter.Memory
                        };

                        instanceCard.SelectionChanged += InstanceCard_SelectionChanged;
                        instanceCard.OperationCompleted += (s, e) => RefreshButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                        _allInstanceCards.Add(instanceCard);
                    }
                }
            }
            else
            {
                foreach (var kvp in instanceReports)
                {
                    Guid instanceId = kvp.Key;
                    InstanceReport report = kvp.Value;

                    var instanceCard = new InstanceCard
                    {
                        InstanceId = instanceId,
                        daemonAddr = currentDaemonConfig!.EndPoint!,
                        DaemonConfig = currentDaemonConfig,
                        InstanceName = report.Config.Name,
                        InstanceType = report.Config.InstanceType.ToString(),
                        Version = report.Config.Version ?? "",
                        Status = report.Status,
                        CpuUsage = report.PerformanceCounter.Cpu,
                        MemoryUsage = report.PerformanceCounter.Memory
                    };

                    // Subscribe to selection change event
                    instanceCard.SelectionChanged += InstanceCard_SelectionChanged;
                    
                    // Subscribe to operation completed event to refresh list
                    instanceCard.OperationCompleted += (s, e) => RefreshButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

                    _allInstanceCards.Add(instanceCard);
                }
                EnableFilterControls();
            }
        }

        private void InstanceCard_SelectionChanged(object? sender, bool isSelected)
        {
            _selectedCount += isSelected ? 1 : -1;
            UpdateBatchOperationBar();
        }

        private void UpdateBatchOperationBar()
        {
            SelectedCountTextBlock.Text = string.Format(Lang.Tr["SelectedCount"], _selectedCount);
            BatchOperationBar.Visibility = _selectedCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ApplyFilters(bool isAutoRefresh = false)
        {
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

            var filteredList = filteredCards.ToList();

            if (isAutoRefresh)
            {
                // Remove items that are no longer in the filtered list
                for (int i = InstanceCardGrid.Items.Count - 1; i >= 0; i--)
                {
                    if (InstanceCardGrid.Items[i] is InstanceCard card && !filteredList.Contains(card))
                    {
                        InstanceCardGrid.Items.RemoveAt(i);
                    }
                }

                // Add items that are in the filtered list but not in the grid
                foreach (var card in filteredList)
                {
                    if (!InstanceCardGrid.Items.Contains(card))
                    {
                        InstanceCardGrid.Items.Add(card);
                    }
                }
            }
            else
            {
                InstanceCardGrid.Items.Clear();
                foreach (var card in filteredList)
                {
                    InstanceCardGrid.Items.Add(card);
                }
            }
        }

        private void ShowLoadingLayer()
        {
            StopTipLayer.Visibility = Visibility.Collapsed;
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

        private void HideLoadingLayer()
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
                LoadingLayer.Visibility = Visibility.Collapsed;
            };
            LoadingLayer.BeginAnimation(OpacityProperty, fadeOutAnimation);
        }
        private void DisableFilterControls()
        {
            DaemonFilter.Visibility = Visibility.Collapsed;
            RunningStatusFilter.Visibility = Visibility.Collapsed;
            SearchTextBox.Visibility = Visibility.Collapsed;
            RefreshButton.Visibility = Visibility.Collapsed;
        }
        private void EnableFilterControls()
        {
            DaemonFilter.Visibility = Visibility.Visible;
            RunningStatusFilter.Visibility = Visibility.Visible;
            SearchTextBox.Visibility = Visibility.Visible;
            RefreshButton.Visibility = Visibility.Visible;
        }
        private void ShowNoDaemonLayer()
        {
            LoadingLayer.Visibility = Visibility.Collapsed;
            InstanceCardGrid.Visibility = Visibility.Collapsed;
            DisableFilterControls();
            StopTipLayer.Visibility = Visibility.Collapsed;
            StopTipLayer.Symbol = "❌";
            StopTipLayer.StopTip = Lang.Tr["FuncDisabled"];
            StopTipLayer.StopDescription = Lang.Tr["FuncDisabledReason_NoDaemon"];
            StopTipLayer.ButtonIcon = SegoeFluentIcons.ConnectApp;
            StopTipLayer.ButtonText = Lang.Tr["ConnectDaemon"];
            StopTipLayer.Visibility = Visibility.Visible;
        }
        private void ShowNoInstanceLayer()
        {
            LoadingLayer.Visibility = Visibility.Collapsed;
            InstanceCardGrid.Visibility = Visibility.Collapsed;
            StopTipLayer.Visibility = Visibility.Collapsed;
            StopTipLayer.Symbol = "🤔";
            StopTipLayer.StopTip = Lang.Tr["NothingHere"];
            StopTipLayer.StopDescription = Lang.Tr["TryAddSomething"];
            StopTipLayer.ButtonIcon = SegoeFluentIcons.AddTo;
            StopTipLayer.ButtonText = Lang.Tr["Main_CreateInstanceNavMenu"];
            StopTipLayer.Visibility = Visibility.Visible;
        }

        private void ShowLoadErrorLayer()
        {
            LoadingLayer.Visibility = Visibility.Collapsed;
            InstanceCardGrid.Visibility = Visibility.Collapsed;
            StopTipLayer.Visibility = Visibility.Collapsed;
            StopTipLayer.Symbol = "❌";
            StopTipLayer.StopTip = Lang.Tr["ConnectDaemonFailedTip"];
            StopTipLayer.StopDescription = Lang.Tr["ConnectDaemonFailedSubTip"];
            StopTipLayer.ButtonIcon = SegoeFluentIcons.Sync;
            StopTipLayer.ButtonText = Lang.Tr["Refresh"];
            StopTipLayer.Visibility = Visibility.Visible;
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
                Title = Lang.Tr["BatchStartConfirmTitle"],
                Content = string.Format(Lang.Tr["BatchStartConfirmContent"], selectedCards.Count),
                PrimaryButtonText = Lang.Tr["Start"],
                CloseButtonText = Lang.Tr["Cancel"],
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
                Lang.Tr["BatchStartComplete"],
                string.Format(Lang.Tr["BatchOperationResult"], successCount, failCount),
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
                Title = Lang.Tr["BatchStopConfirmTitle"],
                Content = string.Format(Lang.Tr["BatchStopConfirmContent"], selectedCards.Count),
                PrimaryButtonText = Lang.Tr["Stop"],
                CloseButtonText = Lang.Tr["Cancel"],
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
                Lang.Tr["BatchStopComplete"],
                string.Format(Lang.Tr["BatchOperationResult"], successCount, failCount),
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
                Title = Lang.Tr["BatchDeleteConfirmTitle"],
                Content = string.Format(Lang.Tr["BatchDeleteConfirmContent"], selectedCards.Count),
                PrimaryButtonText = Lang.Tr["Delete"],
                CloseButtonText = Lang.Tr["Cancel"],
                DefaultButton = ContentDialogButton.Close,
                IsPrimaryButtonEnabled = false
            };

            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            int countdown = 5;
            dialog.PrimaryButtonText = $"{Lang.Tr["Delete"]} ({countdown}s)";

            timer.Tick += (s, args) =>
            {
                countdown--;
                if (countdown > 0)
                {
                    dialog.PrimaryButtonText = $"{Lang.Tr["Delete"]} ({countdown}s)";
                }
                else
                {
                    timer.Stop();
                    dialog.PrimaryButtonText = Lang.Tr["Delete"];
                    dialog.IsPrimaryButtonEnabled = true;
                }
            };
            timer.Start();

            var result = await dialog.ShowAsync();
            timer.Stop();
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
                Lang.Tr["BatchDeleteComplete"],
                string.Format(Lang.Tr["BatchOperationResult"], successCount, failCount),
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