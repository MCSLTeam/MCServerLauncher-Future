using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using iNKORE.UI.WPF.Modern.Controls;
using MCServerLauncher.Common.Contracts.EventRules;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Common.ProtoType.EventTrigger;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Common.ProtoType.Serialization;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Events;
using MCServerLauncher.DaemonClient;
using MCServerLauncher.WPF.Modules;
using MCServerLauncher.WPF.Services;
using RustyOptions;
using Serilog;
using TypedDaemonClient = MCServerLauncher.DaemonClient.DaemonClient;
using TypedInstanceReport = MCServerLauncher.Common.Contracts.Instances.InstanceReport;
using InstanceCommandRequest = MCServerLauncher.Common.Contracts.Instances.InstanceCommandRequest;
using InstanceLogQuery = MCServerLauncher.Common.Contracts.Instances.InstanceLogQuery;
using InstanceReference = MCServerLauncher.Common.Contracts.Instances.InstanceReference;

namespace MCServerLauncher.WPF.InstanceConsole.Modules
{
        /// <summary>
    /// Manages instance data and daemon communication for console window
    /// </summary>
    public class InstanceDataManager
    {
        private static InstanceDataManager? _instance;
        private static readonly object Lock = new();

        private TypedDaemonClient? _daemon;
        private DaemonEventBridge? _eventBridge;
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
        /// Check if daemon is connected
        /// </summary>
        public bool IsConnected => _daemon?.ConnectionState == DaemonConnectionState.Ready;

        public TypedDaemonClient? CurrentDaemon => _daemon;

        /// <summary>
        /// Check if current instance is a Minecraft server
        /// </summary>
        public bool IsMinecraftInstance =>
            CurrentReport?.Config.InstanceType.SupportsMinecraftBoardWidgets() == true;

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

                var connectionResult = await DaemonsWsManager.Get(daemonConfig);
                if (connectionResult.IsErr(out var connectionError))
                    throw DaemonErrorLocalization.ToException(connectionError!);

                _daemon = connectionResult.Unwrap();
                var bridgeResult = await CreateEventBridgeAsync(
                    new DaemonClientEventSource(_daemon),
                    instanceId,
                    DispatchAsync,
                    log => LogReceived?.Invoke(this, log),
                    (title, message, severity) => Notification.Push(
                        title,
                        message,
                        true,
                        severity,
                        Constants.InfoBarPosition.Top,
                        5000,
                        false));
                if (bridgeResult.IsErr(out var bridgeError))
                    throw DaemonErrorLocalization.ToException(bridgeError!);

                _eventBridge = bridgeResult.Unwrap();
                await RefreshInstanceDataAsync();
                _refreshTimer = new Timer(
                    async _ => await RefreshInstanceDataAsync(),
                    null,
                    TimeSpan.FromSeconds(2),
                    TimeSpan.FromSeconds(2));

                Log.Information("[InstanceDataManager] Initialized for instance {0}", instanceId);
            }
            catch (Exception ex)
            {
                if (_eventBridge != null)
                {
                    try
                    {
                        await _eventBridge.DisposeAsync();
                    }
                    catch (Exception disposeException)
                    {
                        Log.Error(disposeException, "[InstanceDataManager] Failed to clean up event bridge after initialization failure");
                    }

                    _eventBridge = null;
                }

                Log.Error(ex, "[InstanceDataManager] Failed to initialize");
                throw;
            }
        }

        internal static Task<Result<DaemonEventBridge, DaemonError>> CreateEventBridgeAsync(
            ITypedDaemonEventSource source,
            Guid instanceId,
            Func<Action, Task> dispatchAsync,
            Action<string> appendLog,
            Action<string, string, InfoBarSeverity> pushNotification,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(dispatchAsync);
            ArgumentNullException.ThrowIfNull(appendLog);
            ArgumentNullException.ThrowIfNull(pushNotification);

            return DaemonEventBridge.CreateAsync(
                source,
                instanceId,
                @event =>
                {
                    if (@event.Meta.Kind != DaemonEventFieldKind.Value ||
                        @event.Data.Kind != DaemonEventFieldKind.Value ||
                        @event.Meta.Value.InstanceId != instanceId)
                        return Task.CompletedTask;

                    return dispatchAsync(() => appendLog(@event.Data.Value.Log));
                },
                @event =>
                {
                    if (@event.Meta.Kind != DaemonEventFieldKind.Value ||
                        @event.Data.Kind != DaemonEventFieldKind.Value ||
                        @event.Meta.Value.SourceInstanceId != instanceId)
                        return Task.CompletedTask;

                    var data = @event.Data.Value;
                    var severity = DaemonEventBridge.MapNotificationSeverity(data.Severity);
                    return dispatchAsync(() => pushNotification(data.Title, data.Message, severity));
                },
                cancellationToken);
        }

        /// <summary>
        /// Refresh instance data from daemon
        /// </summary>
        public async Task RefreshInstanceDataAsync()
        {
            try
            {
                if (_daemon == null || _isDisposed || !IsConnected)
                {
                    await SetCurrentReportAsync(null);
                    return;
                }

                var reportResult = await _daemon.Instances.GetInstanceReportAsync(new InstanceReference(_instanceId), default);
                if (reportResult.IsErr(out var error))
                    throw DaemonErrorLocalization.ToException(error!);

                await SetCurrentReportAsync(ToPresentationReport(reportResult.Unwrap()));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[InstanceDataManager] Failed to refresh instance data");
                await SetCurrentReportAsync(null);
            }
        }

        /// <summary>
        /// Start instance
        /// </summary>
        public async Task StartInstanceAsync()
        {
            try
            {
                var daemon = GetReadyDaemon();
                await EnsureSuccessAsync(daemon.Instances.StartInstanceAsync(new InstanceReference(_instanceId), default));
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
                var daemon = GetReadyDaemon();
                await EnsureSuccessAsync(daemon.Instances.StopInstanceAsync(new InstanceReference(_instanceId), default));
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
                var daemon = GetReadyDaemon();
                await EnsureSuccessAsync(daemon.Instances.HaltInstanceAsync(new InstanceReference(_instanceId), default));
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
                var daemon = GetReadyDaemon();
                await EnsureSuccessAsync(daemon.RestartInstanceAsync(_instanceId));
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
                var daemon = GetReadyDaemon();
                await EnsureSuccessAsync(daemon.Instances.SendCommandAsync(new InstanceCommandRequest(_instanceId, command), default));
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
        public async Task<long?> GetDaemonLatencyAsync()
        {
            try
            {
                var daemon = GetReadyDaemon();
                var started = Stopwatch.GetTimestamp();
                var result = await daemon.PingAsync();
                if (result.IsErr(out var error))
                {
                    Log.Warning(
                        "[InstanceDataManager] Daemon ping failed: {Code}: {Message}",
                        error!.Code,
                        error.Message);
                    return null;
                }

                return (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[InstanceDataManager] Failed to get daemon latency");
                return null;
            }
        }

        /// <summary>
        /// Get instance log history
        /// </summary>
        public async Task<string[]> GetInstanceLogHistoryAsync()
        {
            try
            {
                var daemon = GetReadyDaemon();
                var logResult = await daemon.Instances.GetInstanceLogAsync(new InstanceLogQuery(_instanceId), default);
                if (logResult.IsErr(out var error))
                    throw DaemonErrorLocalization.ToException(error!);

                return logResult.Unwrap().Logs.ToArray();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[InstanceDataManager] Failed to get instance log history");
                throw;
            }
        }

        /// <summary>
        /// Get event rules
        /// </summary>
        public async Task<List<EventRule>> GetEventRulesAsync()
        {
            try
            {
                var daemon = GetReadyDaemon();
                var rulesResult = await daemon.EventRules.GetEventRulesAsync(new EventRuleQuery(_instanceId), default);
                if (rulesResult.IsErr(out var error))
                    throw DaemonErrorLocalization.ToException(error!);

                return JsonSerializer.Deserialize<List<EventRule>>(rulesResult.Unwrap().Rules, StjResolver.CreateDefaultOptions()) ?? [];
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[InstanceDataManager] Failed to get event rules");
                throw;
            }
        }

        /// <summary>
        /// Save event rules
        /// </summary>
        public async Task SaveEventRulesAsync(List<EventRule> rules)
        {
            try
            {
                var daemon = GetReadyDaemon();
                var request = new EventRuleUpdateRequest(
                    _instanceId,
                    JsonSerializer.SerializeToElement(rules, StjResolver.CreateDefaultOptions()));
                await EnsureSuccessAsync(daemon.EventRules.UpdateEventRulesAsync(request, default));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[InstanceDataManager] Failed to save event rules");
                throw;
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

            if (_eventBridge != null)
            {
                try
                {
                    await _eventBridge.DisposeAsync();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[InstanceDataManager] Error unsubscribing from events");
                }

                _eventBridge = null;
            }

            _daemon = null;
            _latestReport = null;
            _instance = null;

            Log.Information("[InstanceDataManager] Disposed");
        }

        private TypedDaemonClient GetReadyDaemon()
        {
            if (_isDisposed || _daemon == null || !IsConnected)
                throw new InvalidOperationException("Daemon is offline or unavailable.");

            return _daemon;
        }

        private static async Task EnsureSuccessAsync(Task<RustyOptions.Result<RustyOptions.Unit, MCServerLauncher.Daemon.API.Errors.DaemonError>> task)
        {
            var result = await task;
            if (result.IsErr(out var error))
                throw DaemonErrorLocalization.ToException(error!);
        }

        private Task SetCurrentReportAsync(InstanceReport? report)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                CurrentReport = report;
                return Task.CompletedTask;
            }

            return dispatcher.InvokeAsync(() => CurrentReport = report).Task;
        }

        private static Task DispatchAsync(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                action();
                return Task.CompletedTask;
            }

            return dispatcher.InvokeAsync(action).Task;
        }

        private static InstanceReport ToPresentationReport(TypedInstanceReport report)
        {
            var config = report.Config;
            return new InstanceReport(
                report.Status,
                new InstanceConfig
                {
                    Name = config.Name,
                    Target = config.Target,
                    InstanceType = config.InstanceType,
                    TargetType = config.TargetType,
                    Version = config.Version,
                    InputEncodingWebName = config.InputEncoding,
                    OutputEncodingWebName = config.OutputEncoding,
                    JavaPath = config.JavaPath,
                    Arguments = config.Arguments.ToArray()
                },
                report.Properties.ToDictionary(pair => pair.Key, pair => pair.Value),
                report.Players.Select(player => new Player(player.Name, player.Uuid)).ToArray(),
                new InstancePerformanceCounter(report.PerformanceCounter.Cpu, report.PerformanceCounter.MemoryBytes));
        }
    }
}
