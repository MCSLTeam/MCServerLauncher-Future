using System.Collections.Immutable;
using MCServerLauncher.Common.Helpers;
using MCServerLauncher.Common.Contracts.System;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.State;
using MCServerLauncher.Daemon.ApplicationCore;
using MCServerLauncher.Daemon.ApplicationCore.Events;
using MCServerLauncher.Daemon.Console;
using MCServerLauncher.Daemon.Management;
using MCServerLauncher.Daemon.Plugins;
using MCServerLauncher.Daemon.Remote;
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

namespace MCServerLauncher.Daemon.Bootstrap;

internal static class DaemonServiceComposition
{
    internal static void ConfigureContainer(
        IRegistrator a,
        IServiceCollection collection,
        HttpService httpService)
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

        var instanceManager = (InstanceManager)InstanceManager.Create();
        // Host-scoped singleton via TouchSocket DI (not process-static Shared).
        a.RegisterSingleton<FileSessionCoordinator>();
        var systemInfoCell = new AsyncTimedLazyCell<SystemInfo>(
            SystemInfoHelper.GetSystemInfo,
            TimeSpan.FromSeconds(2));
        // Full-disk Java discovery is expensive, but newly installed runtimes should appear promptly.
        var javaRuntimeCell = new AsyncTimedLazyCell<JavaRuntimeList>(
            async () => new JavaRuntimeList(
                (await JavaScanner.ScanJavaAsync().ConfigureAwait(false)).ToImmutableArray()),
            TimeSpan.FromSeconds(5));

        a.RegisterSingleton(instanceManager);
        a.RegisterSingleton<IInstanceManager>(instanceManager);
        a.RegisterSingleton<IInstanceSnapshotSource>(instanceManager.InstanceSnapshotSource);
        a.RegisterSingleton(instanceManager.CatalogCommitFeed);
        a.RegisterSingleton(instanceManager.MutationAdmission);
        a.RegisterSingleton<IAsyncTimedLazyCell<SystemInfo>>(systemInfoCell);
        a.RegisterSingleton<IAsyncTimedLazyCell<JavaRuntimeList>>(javaRuntimeCell);

        a.RegisterSingleton(DomainEventDispatchPolicy.Default);
        a.RegisterSingleton<IDomainEventPort, DomainEventPort>();
        a.RegisterSingleton<IInstanceApplication, LocalInstanceApplication>();
        a.RegisterSingleton<IFileApplication, LocalFileApplication>();
        a.RegisterSingleton<ISystemApplication, LocalSystemApplication>();
        a.RegisterSingleton<IEventRuleApplication, LocalEventRuleApplication>();
        a.RegisterSingleton<IDaemonApplication, LocalDaemonApplication>();
        a.RegisterSingleton<IDaemonRuntimeLifecycle, LocalDaemonRuntimeLifecycle>();
        a.RegisterSingleton<IPluginEventBus, PluginEventBus>();
        a.RegisterSingleton<PluginHost>();
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
            services.GetRequiredService<TimeProvider>(),
            downloadSessionLimit: AppConfig.Get().FileDownloadSessions));
        collection.AddSingleton<IV2ConnectionAdministration>(services =>
            services.GetRequiredService<TouchSocketV2TransportPlugin>());
        a.RegisterSingleton<InstanceDomainEventBridge>();
        a.RegisterSingleton<InstanceCatalogDomainEventBridge>();
        a.RegisterSingleton<EventTriggerService>();
        a.RegisterSingleton<DaemonReportPublisher>();
    }

    internal static void ConfigurePlugins(IPluginManager a)
    {
        a.Add<HttpPlugin>();
        var v2Options = new WebSocketFeatureOptions();
        v2Options.SetUrl(TouchSocketV2TransportPlugin.Endpoint);
        v2Options.SetAutoPong(true);
        v2Options.VerifyConnection = WsVerifyHandler.VerifyV2Handler;
        a.Add(new WebSocketFeature(v2Options));

        a.Add<TouchSocketV2TransportPlugin>();
        a.UseDefaultHttpServicePlugin();
    }

    internal static DaemonLifecycleAttachment AttachDaemonLifecycle(HttpService httpService)
    {
        var pluginHost = httpService.Resolver.GetRequiredService<PluginHost>();
        pluginHost.StartAsync(CancellationToken.None).GetAwaiter().GetResult();

        // Setup completes before ServeAsync starts the HTTP/WebSocket listener, so resolving here
        // guarantees the final catalog is frozen and published before any connection is accepted.
        _ = httpService.Resolver.GetRequiredService<BuiltInProtocolCatalogComposition>();
        var remoteEvents = httpService.Resolver.GetRequiredService<V2RemoteEventBridge>();
        pluginHost.Activate(
            httpService.Resolver.GetRequiredService<BuiltInProtocolCatalogComposition>().Catalog,
            remoteEvents);
        return new DaemonLifecycleAttachment(
            pluginHost,
            httpService.Resolver.GetRequiredService<FileSessionCoordinator>(),
            httpService.Resolver.GetRequiredService<EventTriggerService>(),
            httpService.Resolver.GetRequiredService<InstanceDomainEventBridge>(),
            httpService.Resolver.GetRequiredService<InstanceCatalogDomainEventBridge>(),
            httpService.Resolver.GetRequiredService<DaemonReportPublisher>(),
            httpService.Resolver.GetRequiredService<ConsoleApplication>(),
            remoteEvents,
            httpService.Resolver.GetRequiredService<IV2ConnectionAdministration>());
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
        PluginHost plugins,
        FileSessionCoordinator files,
        EventTriggerService eventTrigger,
        InstanceDomainEventBridge instanceEvents,
        InstanceCatalogDomainEventBridge catalogEvents,
        DaemonReportPublisher reports,
        ConsoleApplication console,
        V2RemoteEventBridge remoteEvents,
        IV2ConnectionAdministration connections)
        : this(
            () =>
            {
                files.Start();
                _ = eventTrigger;
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
                    await connections.ShutdownAsync().ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }

                try
                {
                    await plugins.StopAsync(CancellationToken.None).ConfigureAwait(false);
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
