using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.DaemonClient;
using MCServerLauncher.WPF.Modules;
using Serilog;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace MCServerLauncher.WPF.InstanceConsole.Modules
{
    /// <summary>
    /// Manages instance data and daemon communication for console window
    /// </summary>
    public class InstanceDataManager
    {
        private static InstanceDataManager? _instance;
        private static readonly object Lock = new();

        private IDaemon? _daemon;
        private Guid _instanceId;
        private Constants.DaemonConfigModel? _daemonConfig;
        private InstanceReport? _latestReport;
        private Timer? _refreshTimer;
        private bool _isDisposed;

        public static InstanceDataManager Instance
        {
            get
            {
                lock (Lock)
                {
                    return _instance ??= new InstanceDataManager();
                }
            }
        }

        private InstanceDataManager()
        {
        }

        /// <summary>
        /// Current instance report
        /// </summary>
        public InstanceReport? CurrentReport
        {
            get => _latestReport;
            private set
            {
                _latestReport = value;
                ReportUpdated?.Invoke(this, value);
            }
        }

        /// <summary>
        /// Current instance ID
        /// </summary>
        public Guid InstanceId => _instanceId;

        /// <summary>
        /// Check if current instance is a Minecraft server
        /// </summary>
        public bool IsMinecraftInstance
        {
            get
            {
                if (CurrentReport == null)
                    return false;

                var mcTypes = new[]
                {
                    InstanceType.MCJava,
                    InstanceType.MCFabric,
                    InstanceType.MCForge,
                    InstanceType.MCNeoForge,
                    InstanceType.MCQuilt,
                    InstanceType.MCCleanroom,
                    InstanceType.MCSponge,
                    InstanceType.MCVanilla,
                    InstanceType.MCCraftBukkit,
                    InstanceType.MCSpigot,
                    InstanceType.MCPaper,
                    InstanceType.MCLeaf,
                    InstanceType.MCLeaves,
                    InstanceType.MCFolia,
                    InstanceType.MCPufferfish,
                    InstanceType.MCPurpur,
                    InstanceType.MCMohist
                };

                return System.Linq.Enumerable.Contains(mcTypes, CurrentReport.Config.InstanceType);
            }
        }

        /// <summary>
        /// Event fired when instance report is updated
        /// </summary>
        public event EventHandler<InstanceReport?>? ReportUpdated;

        /// <summary>
        /// Event fired when instance log is received
        /// </summary>
        public event EventHandler<string>? LogReceived;

        /// <summary>
        /// Initialize manager with daemon and instance information
        /// </summary>
        public async Task InitializeAsync(Constants.DaemonConfigModel daemonConfig, Guid instanceId)
        {
            try
            {
                _daemonConfig = daemonConfig;
                _instanceId = instanceId;

                // Get daemon connection
                _daemon = await DaemonsWsManager.Get(daemonConfig);
                if (_daemon == null)
                {
                    Log.Error("[InstanceDataManager] Failed to connect to daemon");
                    throw new InvalidOperationException("Failed to connect to daemon");
                }

                // Subscribe to instance logs
                _daemon.InstanceLogEvent += OnInstanceLog;

                await _daemon.SubscribeEvent(
                    Common.ProtoType.Event.EventType.InstanceLog,
                    new Common.ProtoType.Event.InstanceLogEventMeta { InstanceId = instanceId }
                );

                // Initial data load
                await RefreshInstanceDataAsync();

                // Start periodic refresh (every 2 seconds)
                _refreshTimer = new Timer(async _ => await RefreshInstanceDataAsync(), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));

                Log.Information("[InstanceDataManager] Initialized for instance {0}", instanceId);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[InstanceDataManager] Failed to initialize");
                throw;
            }
        }

        /// <summary>
        /// Refresh instance data from daemon
        /// </summary>
        public async Task RefreshInstanceDataAsync()
        {
            try
            {
                if (_daemon == null || _isDisposed)
                    return;

                var report = await _daemon.GetInstanceReportAsync(_instanceId);
                CurrentReport = report;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[InstanceDataManager] Failed to refresh instance data");
            }
        }

        /// <summary>
        /// Start instance
        /// </summary>
        public async Task StartInstanceAsync()
        {
            try
            {
                if (_daemon == null)
                    throw new InvalidOperationException("Daemon not initialized");

                await _daemon.StartInstanceAsync(_instanceId);
                Log.Information("[InstanceDataManager] Started instance {0}", _instanceId);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[InstanceDataManager] Failed to start instance");
                throw;
            }
        }

        /// <summary>
        /// Stop instance
        /// </summary>
        public async Task StopInstanceAsync()
        {
            try
            {
                if (_daemon == null)
                    throw new InvalidOperationException("Daemon not initialized");

                await _daemon.StopInstanceAsync(_instanceId);
                Log.Information("[InstanceDataManager] Stopped instance {0}", _instanceId);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[InstanceDataManager] Failed to stop instance");
                throw;
            }
        }

        /// <summary>
        /// Kill instance
        /// </summary>
        public async Task KillInstanceAsync()
        {
            try
            {
                if (_daemon == null)
                    throw new InvalidOperationException("Daemon not initialized");

                await _daemon.KillInstanceAsync(_instanceId);
                Log.Information("[InstanceDataManager] Killed instance {0}", _instanceId);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[InstanceDataManager] Failed to kill instance");
                throw;
            }
        }

        /// <summary>
        /// Restart instance
        /// </summary>
        public async Task RestartInstanceAsync()
        {
            try
            {
                if (_daemon == null)
                    throw new InvalidOperationException("Daemon not initialized");

                await _daemon.RestartInstanceAsync(_instanceId);
                Log.Information("[InstanceDataManager] Restarted instance {0}", _instanceId);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[InstanceDataManager] Failed to restart instance");
                throw;
            }
        }

        /// <summary>
        /// Send command to instance
        /// </summary>
        public async Task SendCommandAsync(string command)
        {
            try
            {
                if (_daemon == null)
                    throw new InvalidOperationException("Daemon not initialized");

                await _daemon.SentToInstanceAsync(_instanceId, command);
                Log.Debug("[InstanceDataManager] Sent command to instance {0}: {1}", _instanceId, command);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[InstanceDataManager] Failed to send command");
                throw;
            }
        }

        /// <summary>
        /// Get daemon latency
        /// </summary>
        public async Task<long> GetDaemonLatencyAsync()
        {
            try
            {
                if (_daemon == null)
                    throw new InvalidOperationException("Daemon not initialized");

                return await _daemon.PingAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[InstanceDataManager] Failed to get daemon latency");
                return -1;
            }
        }

        private void OnInstanceLog(Guid instanceId, string log)
        {
            if (instanceId == _instanceId)
            {
                LogReceived?.Invoke(this, log);
            }
        }

        /// <summary>
        /// Dispose resources
        /// </summary>
        public async Task DisposeAsync()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;

            _refreshTimer?.Dispose();
            _refreshTimer = null;

            if (_daemon != null)
            {
                try
                {
                    await _daemon.UnSubscribeEvent(
                        Common.ProtoType.Event.EventType.InstanceLog,
                        new Common.ProtoType.Event.InstanceLogEventMeta { InstanceId = _instanceId }
                    );
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[InstanceDataManager] Error unsubscribing from events");
                }

                _daemon.InstanceLogEvent -= OnInstanceLog;
            }

            _daemon = null;
            _latestReport = null;

            Log.Information("[InstanceDataManager] Disposed");
        }
    }
}
