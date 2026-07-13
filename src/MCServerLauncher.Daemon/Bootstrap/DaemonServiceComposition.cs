using MCServerLauncher.Common.Helpers;
using MCServerLauncher.Common.ProtoType;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.State;
using MCServerLauncher.Daemon.ApplicationCore;
using MCServerLauncher.Daemon.ApplicationCore.Events;
using MCServerLauncher.Daemon.Console;
using MCServerLauncher.Daemon.Management;
using MCServerLauncher.Daemon.Remote;
using MCServerLauncher.Daemon.Remote.Action;
using MCServerLauncher.Daemon.Remote.Event;
using MCServerLauncher.Daemon.Remote.Rpc.Catalog;
using MCServerLauncher.Daemon.Remote.Rpc.Dispatch;
using MCServerLauncher.Daemon.Remote.Rpc.Events;
using MCServerLauncher.Daemon.Remote.Rpc.Transport;
using MCServerLauncher.Daemon.Storage;
using MCServerLauncher.Daemon.Utils.LazyCell;
using MCServerLauncher.Daemon.Utils.Status;
using MessagePipe;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TouchSocket.Core;
using TouchSocket.Http;
using TouchSocket.Http.WebSockets;
using LegacySystemInfo = MCServerLauncher.Common.ProtoType.Status.SystemInfo;

namespace MCServerLauncher.Daemon.Bootstrap;

internal static class DaemonServiceComposition
{
    internal static void ConfigureContainer(
        IRegistrator a,
        IServiceCollection collection,
        HttpService httpService,
        ActionHandlerRegistrySnapshot selectedRegistry,
        LegacyEventQueueControl? legacyEventQueueControl = null)
    {
        collection.AddMessagePipe(options =>
        {
            options.EnableAutoRegistration = false;
            options.DefaultAsyncPublishStrategy = AsyncPublishStrategy.Sequential;
            options.InstanceLifetime = InstanceLifetime.Singleton;
            options.EnableCaptureStackTrace = false;
        });

        a.RegisterSingleton(collection);
        a.RegisterSingleton<ConsoleApplication>();
        a.RegisterSingleton<GracefulShutdown>();
        a.RegisterSingleton<IHttpService>(httpService);
        a.RegisterSingleton(selectedRegistry);
        a.RegisterSingleton(legacyEventQueueControl ?? new LegacyEventQueueControl());
        a.RegisterSingleton<IActionExecutor, AnotherActionExecutor>();
        a.RegisterSingleton<IEventService, EventService>();
        a.RegisterSingleton<WsContextContainer>();

        var instanceManager = (InstanceManager)InstanceManager.Create();
        var fileSessionCoordinator = FileSessionCoordinator.Shared;
        fileSessionCoordinator.ConfigureDownloadSessionLimit(AppConfig.Get().FileDownloadSessions);
        var systemInfoCell = new AsyncTimedLazyCell<LegacySystemInfo>(
            SystemInfoHelper.GetSystemInfo,
            TimeSpan.FromSeconds(2));
        var javaRuntimeCell = new AsyncTimedLazyCell<JavaInfo[]>(
            JavaScanner.ScanJavaAsync,
            TimeSpan.FromSeconds(2));

        a.RegisterSingleton(instanceManager);
        a.RegisterSingleton<IInstanceManager>(instanceManager);
        a.RegisterSingleton<IInstanceSnapshotSource>(instanceManager.InstanceSnapshotSource);
        a.RegisterSingleton(instanceManager.CatalogCommitFeed);
        a.RegisterSingleton(instanceManager.MutationAdmission);
        a.RegisterSingleton(fileSessionCoordinator);
        a.RegisterSingleton<IAsyncTimedLazyCell<LegacySystemInfo>>(systemInfoCell);
        a.RegisterSingleton<IAsyncTimedLazyCell<JavaInfo[]>>(javaRuntimeCell);

        a.RegisterSingleton(DomainEventDispatchPolicy.Default);
        a.RegisterSingleton<IDomainEventPort, DomainEventPort>();
        a.RegisterSingleton<IInstanceApplication, LocalInstanceApplication>();
        a.RegisterSingleton<IFileApplication, LocalFileApplication>();
        a.RegisterSingleton<ISystemApplication, LocalSystemApplication>();
        a.RegisterSingleton<LegacySystemActionAdapter>();
        a.RegisterSingleton<IEventRuleApplication, LocalEventRuleApplication>();
        a.RegisterSingleton<IDaemonApplication, LocalDaemonApplication>();
        a.RegisterSingleton<IDaemonRuntimeLifecycle, LocalDaemonRuntimeLifecycle>();
        var protocolCatalogAccessor = new FrozenProtocolCatalogAccessor();
        a.RegisterSingleton(protocolCatalogAccessor);
        a.RegisterSingleton<IFrozenProtocolCatalogAccessor>(protocolCatalogAccessor);
        var v2Runtime = new V2TransportRuntime(protocolCatalogAccessor);
        a.RegisterSingleton(TimeProvider.System);
        a.RegisterSingleton<BuiltInProtocolCatalogComposition>();
        collection.AddSingleton(v2Runtime);
        collection.AddSingleton(_ => v2Runtime.GetEventConnections());
        collection.AddSingleton<IV2RpcDiagnosticSink>(services => new V2RpcLoggingDiagnosticSink(
            services.GetRequiredService<ILogger<V2RpcLoggingDiagnosticSink>>()));
        collection.AddSingleton<IV2InboundDiagnosticSink>(services => new V2InboundLoggingDiagnosticSink(
            services.GetRequiredService<ILogger<V2InboundLoggingDiagnosticSink>>()));
        collection.AddSingleton(services => new V2RemoteEventBridge(
            services.GetRequiredService<IDomainEventPort>(),
            protocolCatalogAccessor.GetRequired(),
            v2Runtime.GetEventConnections(),
            services.GetRequiredService<TimeProvider>()));
        collection.AddSingleton(services => new TouchSocketV2TransportPlugin(
            services.GetRequiredService<IDaemonApplication>(),
            protocolCatalogAccessor,
            v2Runtime,
            services.GetRequiredService<IV2RpcDiagnosticSink>(),
            services.GetRequiredService<IV2InboundDiagnosticSink>(),
            services.GetRequiredService<TimeProvider>()));
        a.RegisterSingleton<InstanceDomainEventBridge>();
        a.RegisterSingleton<InstanceCatalogDomainEventBridge>();
        a.RegisterSingleton<EventTriggerService>();
        a.RegisterSingleton<LegacyDomainEventAdapter>();
        a.RegisterSingleton<DaemonReportPublisher>();
    }

    internal static void ConfigurePlugins(
        IPluginManager a,
        LegacyEventQueueControl? legacyEventQueueControl = null)
    {
        a.Add<HttpPlugin>();
        var v2Options = new WebSocketFeatureOptions();
        v2Options.SetUrl(TouchSocketV2TransportPlugin.Endpoint);
        v2Options.SetAutoPong(true);
        v2Options.VerifyConnection = WsVerifyHandler.VerifyV2Handler;
        a.Add(new WebSocketFeature(v2Options));
        var v1Options = new WebSocketFeatureOptions();
        v1Options.SetUrl("/api/v1");
        v1Options.SetAutoPong(true);
        v1Options.VerifyConnection = WsVerifyHandler.VerifyHandler;
        a.Add(new WebSocketFeature(v1Options));

        a.Add<TouchSocketV2TransportPlugin>();
        a.Add<WsBasePlugin>();
        a.Add<WsActionPlugin>();
        var wsEventPlugin = a.Add<WsEventPlugin>();
        (legacyEventQueueControl ?? throw new InvalidOperationException(
            "Legacy event queue control must be provided when configuring plugins."))
            .Attach(wsEventPlugin);
        a.Add<WsExpirationPlugin>(); // WsExpirePlugin注册必须在WsBasePlugin之后
        a.UseDefaultHttpServicePlugin();
    }

    internal static DaemonLifecycleAttachment AttachDaemonLifecycle(HttpService httpService)
    {
        // Setup completes before ServeAsync starts the HTTP/WebSocket listener, so resolving here
        // guarantees the final catalog is frozen and published before any connection is accepted.
        _ = httpService.Resolver.GetRequiredService<BuiltInProtocolCatalogComposition>();
        return new DaemonLifecycleAttachment(
            httpService.Resolver.GetRequiredService<FileSessionCoordinator>(),
            httpService.Resolver.GetRequiredService<EventTriggerService>(),
            httpService.Resolver.GetRequiredService<LegacyDomainEventAdapter>(),
            httpService.Resolver.GetRequiredService<InstanceDomainEventBridge>(),
            httpService.Resolver.GetRequiredService<InstanceCatalogDomainEventBridge>(),
            httpService.Resolver.GetRequiredService<DaemonReportPublisher>(),
            httpService.Resolver.GetRequiredService<ConsoleApplication>(),
            httpService.Resolver.GetRequiredService<V2RemoteEventBridge>(),
            httpService.Resolver.GetRequiredService<TouchSocketV2TransportPlugin>());
    }
}

internal enum DaemonLifecycleState
{
    Created,
    Starting,
    Started,
    Stopping,
    Stopped,
    Disposed
}

internal sealed class DaemonLifecycleAttachment : IAsyncDisposable
{
    private readonly SemaphoreSlim _transitionGate = new(1, 1);
    private readonly Func<Task> _startCore;
    private readonly Func<Task> _stopCore;
    private int _state = (int)DaemonLifecycleState.Created;

    internal DaemonLifecycleAttachment(
        FileSessionCoordinator files,
        EventTriggerService eventTrigger,
        LegacyDomainEventAdapter legacyEvents,
        InstanceDomainEventBridge instanceEvents,
        InstanceCatalogDomainEventBridge catalogEvents,
        DaemonReportPublisher reports,
        ConsoleApplication console,
        V2RemoteEventBridge remoteEvents,
        TouchSocketV2TransportPlugin v2Transport)
        : this(
            () =>
            {
                files.Start();
                _ = eventTrigger;
                _ = legacyEvents;
                _ = instanceEvents;
                catalogEvents.Start();
                reports.Start();
                console.Serve();
                return Task.CompletedTask;
            },
            async () =>
            {
                List<Exception> failures = [];
                try
                {
                    remoteEvents.Dispose();
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }

                try
                {
                    await v2Transport.ShutdownAsync().ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }

                try
                {
                    reports.RequestStop();
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }

                if (failures.Count != 0)
                    throw new AggregateException("One or more daemon lifecycle stop steps failed.", failures);
            })
    {
    }

    internal DaemonLifecycleAttachment(Func<Task> startCore, Func<Task> stopCore)
    {
        _startCore = startCore ?? throw new ArgumentNullException(nameof(startCore));
        _stopCore = stopCore ?? throw new ArgumentNullException(nameof(stopCore));
    }

    internal int StartCount { get; private set; }
    internal int StopCount { get; private set; }
    internal DaemonLifecycleState State => (DaemonLifecycleState)Volatile.Read(ref _state);

    internal async Task<bool> StartAsync()
    {
        await _transitionGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (State != DaemonLifecycleState.Created)
                return false;

            SetState(DaemonLifecycleState.Starting);
            StartCount++;
            try
            {
                await _startCore().ConfigureAwait(false);
                SetState(DaemonLifecycleState.Started);
                return true;
            }
            catch (Exception startException)
            {
                SetState(DaemonLifecycleState.Stopping);
                StopCount++;
                try
                {
                    await _stopCore().ConfigureAwait(false);
                }
                catch (Exception stopException)
                {
                    throw new AggregateException("Daemon lifecycle startup and rollback both failed.",
                        startException, stopException);
                }
                finally
                {
                    SetState(DaemonLifecycleState.Stopped);
                }

                throw;
            }
        }
        finally
        {
            _transitionGate.Release();
        }
    }

    internal async Task StopAsync()
    {
        await _transitionGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (State is DaemonLifecycleState.Stopped or DaemonLifecycleState.Disposed)
                return;

            SetState(DaemonLifecycleState.Stopping);
            StopCount++;
            try
            {
                await _stopCore().ConfigureAwait(false);
            }
            finally
            {
                SetState(DaemonLifecycleState.Stopped);
            }
        }
        finally
        {
            _transitionGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await StopAsync().ConfigureAwait(false);
        }
        finally
        {
            await _transitionGate.WaitAsync().ConfigureAwait(false);
            try
            {
                SetState(DaemonLifecycleState.Disposed);
            }
            finally
            {
                _transitionGate.Release();
            }
        }
    }

    private void SetState(DaemonLifecycleState state) => Volatile.Write(ref _state, (int)state);
}
