using iNKORE.UI.WPF.Modern.Common.IconKeys;
using MCServerLauncher.WPF.Modules;
using MCServerLauncher.WPF.ViewModels;
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace MCServerLauncher.WPF.View.Pages
{
    public partial class InstanceManagerPage
    {
        private readonly InstanceManagerViewModel _viewModel;
        private System.Windows.Threading.DispatcherTimer? _refreshTimer;

        public InstanceManagerPage()
        {
            _viewModel = App.ViewModelLocator.InstanceManager;
            InitializeComponent();
            DataContext = _viewModel;

            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
            _viewModel.LoadDaemonFilterItems();

            IsVisibleChanged += async (s, e) =>
            {
                if (IsVisible)
                {
                    await _viewModel.RefreshCommand.ExecuteAsync(null);
                    StartAutoRefresh();
                }
                else
                {
                    StopAutoRefresh();
                }
            };
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(InstanceManagerViewModel.IsLoading):
                    if (_viewModel.IsLoading) ShowLoadingLayer();
                    else HideLoadingLayer();
                    break;
                case nameof(InstanceManagerViewModel.ErrorState):
                    UpdateErrorState();
                    break;
                case nameof(InstanceManagerViewModel.SelectedCount):
                    SelectedCountTextBlock.Text = string.Format(Lang.Tr["SelectedCount"], _viewModel.SelectedCount);
                    BatchOperationBar.Visibility = _viewModel.SelectedCount > 0 ? Visibility.Visible : Visibility.Collapsed;
                    break;
            }
        }

        private void UpdateErrorState()
        {
            StopTipLayer.Visibility = Visibility.Collapsed;
            InstanceCardGrid.Visibility = Visibility.Visible;
            FilterBar.Visibility = Visibility.Visible;

            switch (_viewModel.ErrorState)
            {
                case "no_daemon":
                    InstanceCardGrid.Visibility = Visibility.Collapsed;
                    FilterBar.Visibility = Visibility.Collapsed;
                    StopTipLayer.Symbol = "❌";
                    StopTipLayer.StopTip = Lang.Tr["FuncDisabled"];
                    StopTipLayer.StopDescription = Lang.Tr["FuncDisabledReason_NoDaemon"];
                    StopTipLayer.ButtonIcon = SegoeFluentIcons.ConnectApp;
                    StopTipLayer.ButtonText = Lang.Tr["ConnectDaemon"];
                    StopTipLayer.Visibility = Visibility.Visible;
                    break;
                case "no_instance":
                    InstanceCardGrid.Visibility = Visibility.Collapsed;
                    StopTipLayer.Symbol = "🤔";
                    StopTipLayer.StopTip = Lang.Tr["NothingHere"];
                    StopTipLayer.StopDescription = Lang.Tr["TryAddSomething"];
                    StopTipLayer.ButtonIcon = SegoeFluentIcons.AddTo;
                    StopTipLayer.ButtonText = Lang.Tr["Main_CreateInstanceNavMenu"];
                    StopTipLayer.Visibility = Visibility.Visible;
                    break;
                case "load_error":
                    InstanceCardGrid.Visibility = Visibility.Collapsed;
                    StopTipLayer.Symbol = "❌";
                    StopTipLayer.StopTip = Lang.Tr["ConnectDaemonFailedTip"];
                    StopTipLayer.StopDescription = Lang.Tr["ConnectDaemonFailedSubTip"];
                    StopTipLayer.ButtonIcon = SegoeFluentIcons.Sync;
                    StopTipLayer.ButtonText = Lang.Tr["Refresh"];
                    StopTipLayer.Visibility = Visibility.Visible;
                    break;
            }
        }

        private void StartAutoRefresh()
        {
            var interval = SettingsManager.Get.Instance.AutoRefreshInterval;
            if (interval <= 0) { StopAutoRefresh(); return; }

            if (_refreshTimer == null)
            {
                _refreshTimer = new System.Windows.Threading.DispatcherTimer();
                _refreshTimer.Tick += async (s, e) => await _viewModel.AutoRefreshCommand.ExecuteAsync(null);
            }
            _refreshTimer.Interval = TimeSpan.FromSeconds(interval);
            _refreshTimer.Start();
        }

        private void StopAutoRefresh()
        {
            _refreshTimer?.Stop();
        }

        private void DaemonFilterChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is not ComboBox) return;
            _ = _viewModel.RefreshCommand.ExecuteAsync(null);
        }

        private void RunningStatusFilterChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_viewModel == null) return;
            if (RunningStatusFilter.SelectedItem is ComboBoxItem selectedItem)
            {
                _viewModel.SelectedStatusFilter = selectedItem.Tag?.ToString() ?? "All";
                _viewModel.ApplyFilters();
            }
        }

        private void ShowLoadingLayer()
        {
            StopTipLayer.Visibility = Visibility.Collapsed;
            InstanceCardGrid.Visibility = Visibility.Visible;
            LoadingLayer.Visibility = Visibility.Visible;
            var fadeIn = new DoubleAnimation(0.0, 1.0, new Duration(TimeSpan.FromSeconds(0.4))) { FillBehavior = FillBehavior.HoldEnd };
            LoadingLayer.BeginAnimation(OpacityProperty, fadeIn);
        }

        private void HideLoadingLayer()
        {
            var fadeOut = new DoubleAnimation(1.0, 0.0, new Duration(TimeSpan.FromSeconds(0.4))) { FillBehavior = FillBehavior.HoldEnd };
            fadeOut.Completed += (s, e) => LoadingLayer.Visibility = Visibility.Collapsed;
            LoadingLayer.BeginAnimation(OpacityProperty, fadeOut);
        }
    }
}
