using MCServerLauncher.WPF.InstanceConsole.Modules;
using Serilog;
using System;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace MCServerLauncher.WPF.InstanceConsole.View.Components
{
    /// <summary>
    /// Daemon connection info component
    /// </summary>
    public partial class DaemonConnectionInfo : IInstanceBoardComponent
    {
        private bool _isLoading;
        private bool _hasError;
        private DispatcherTimer? _refreshTimer;

        public DaemonConnectionInfo()
        {
            InitializeComponent();
        }

        public bool IsLoading
        {
            get => _isLoading;
            private set => _isLoading = value;
        }

        public bool HasError
        {
            get => _hasError;
            private set => _hasError = value;
        }

        public async Task InitializeAsync()
        {
            try
            {
                IsLoading = true;
                HasError = false;

                // Start periodic refresh (every 5 seconds)
                _refreshTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(5)
                };
                _refreshTimer.Tick += async (s, e) => await RefreshAsync();
                _refreshTimer.Start();

                await RefreshAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[DaemonConnectionInfo] Failed to initialize");
                HasError = true;
            }
            finally
            {
                IsLoading = false;
            }
        }

        public async Task RefreshAsync()
        {
            try
            {
                var latency = await InstanceDataManager.Instance.GetDaemonLatencyAsync();
                
                Dispatcher.Invoke(() =>
                {
                    if (latency >= 0)
                    {
                        WebSocketPingStatusTextBlock.Text = $"{latency} ms";
                    }
                    else
                    {
                        WebSocketPingStatusTextBlock.Text = "-- ms";
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[DaemonConnectionInfo] Failed to refresh");
                HasError = true;
                
                Dispatcher.Invoke(() =>
                {
                    WebSocketPingStatusTextBlock.Text = "Error";
                });
            }
        }
    }
}
