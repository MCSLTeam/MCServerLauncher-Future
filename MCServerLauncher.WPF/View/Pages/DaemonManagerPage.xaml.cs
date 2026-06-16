using MCServerLauncher.WPF.ViewModels;
using MCServerLauncher.WPF.ViewModels.Models;
using System;
using System.Windows;
using System.Windows.Controls;

namespace MCServerLauncher.WPF.View.Pages
{
    public partial class DaemonManagerPage
    {
        private readonly DaemonManagerViewModel _viewModel;
        private System.Windows.Threading.DispatcherTimer? _refreshTimer;

        public DaemonManagerPage()
        {
            InitializeComponent();
            _viewModel = App.ViewModelLocator.DaemonManager;
            DataContext = _viewModel;

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

            _viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName is nameof(DaemonManagerViewModel.AutoRefreshEnabled)
                    or nameof(DaemonManagerViewModel.RefreshIntervalSeconds))
                {
                    StartAutoRefresh();
                }
            };
        }

        private void StartAutoRefresh()
        {
            if (!IsVisible || !_viewModel.AutoRefreshEnabled)
            {
                StopAutoRefresh();
                return;
            }

            _refreshTimer ??= new System.Windows.Threading.DispatcherTimer();
            _refreshTimer.Tick -= RefreshTimerTick;
            _refreshTimer.Tick += RefreshTimerTick;
            _refreshTimer.Interval = TimeSpan.FromSeconds(Math.Max(1, _viewModel.RefreshIntervalSeconds));
            _refreshTimer.Start();
        }

        private async void RefreshTimerTick(object? sender, EventArgs e)
        {
            await _viewModel.AutoRefreshCommand.ExecuteAsync(null);
        }

        private void StopAutoRefresh()
        {
            _refreshTimer?.Stop();
        }

        private async void EditDaemonMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem { Tag: DaemonCardModel daemon })
            {
                await _viewModel.EditDaemonCommand.ExecuteAsync(daemon);
            }
        }

        private async void DeleteDaemonMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem { Tag: DaemonCardModel daemon })
            {
                await _viewModel.DeleteDaemonCommand.ExecuteAsync(daemon);
            }
        }
    }
}
