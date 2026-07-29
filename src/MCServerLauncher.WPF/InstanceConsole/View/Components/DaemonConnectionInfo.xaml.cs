using MCServerLauncher.WPF.InstanceConsole.Modules;
using MCServerLauncher.WPF.Modules;
using Serilog;
using System;
using System.Threading;
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
        private readonly object _refreshLock = new();
        private CancellationTokenSource _refreshCancellation = new();
        private Task? _refreshTask;
        private bool _isDisposed;

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
                lock (_refreshLock)
                {
                    if (_isDisposed)
                    {
                        _refreshCancellation = new CancellationTokenSource();
                        _refreshTask = null;
                    }

                    _isDisposed = false;
                }

                // Start periodic refresh (every 5 seconds)
                _refreshTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(5)
                };
                _refreshTimer.Tick += RefreshTimer_Tick;
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

        public Task RefreshAsync()
        {
            lock (_refreshLock)
            {
                if (_isDisposed)
                    return Task.CompletedTask;

                if (_refreshTask is { IsCompleted: false })
                    return _refreshTask;

                _refreshTask = RefreshCoreAsync(_refreshCancellation.Token);
                return _refreshTask;
            }
        }

        private async Task RefreshCoreAsync(CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var latency = await InstanceDataManager.Instance.GetDaemonLatencyAsync(cancellationToken);
                
                cancellationToken.ThrowIfCancellationRequested();
                Dispatcher.Invoke(() =>
                {
                    if (latency.HasValue)
                    {
                        WebSocketPingStatusTextBlock.Text = $"{latency.Value} ms";
                    }
                    else
                    {
                        WebSocketPingStatusTextBlock.Text = Lang.Tr["Status_LoadFailed"];
                    }
                });
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                if (cancellationToken.IsCancellationRequested)
                    return;

                Log.Error(ex, "[DaemonConnectionInfo] Failed to refresh");
                HasError = true;
                
                Dispatcher.Invoke(() =>
                {
                    WebSocketPingStatusTextBlock.Text = "Error";
                });
            }
        }

        private async void RefreshTimer_Tick(object? sender, EventArgs e)
        {
            await RefreshAsync();
        }

        public async Task DisposeAsync()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            if (_refreshTimer != null)
            {
                _refreshTimer.Stop();
                _refreshTimer.Tick -= RefreshTimer_Tick;
                _refreshTimer = null;
            }

            Task? refreshTask;
            lock (_refreshLock)
            {
                _refreshCancellation.Cancel();
                refreshTask = _refreshTask;
            }

            if (refreshTask != null)
                await refreshTask;

            lock (_refreshLock)
            {
                _refreshCancellation.Dispose();
            }
        }
    }
}
