using System.Net;
using System.Reflection;
using MCServerLauncher.Daemon;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Protocol;
using MCServerLauncher.Daemon.API.State;
using MCServerLauncher.Daemon.ApplicationCore;
using MCServerLauncher.Daemon.Bootstrap;
using MCServerLauncher.Daemon.Management;
using MCServerLauncher.Daemon.Remote.Action;
using MCServerLauncher.Daemon.Remote.Event;
using MCServerLauncher.Daemon.Remote.Rpc.Catalog;
using MCServerLauncher.Daemon.Remote.Rpc.Dispatch;
using MCServerLauncher.Daemon.Remote.Rpc.Events;
using MCServerLauncher.Daemon.Remote.Rpc.Transport;
using MCServerLauncher.Daemon.Storage;
using MCServerLauncher.ProtocolTests.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TouchSocket.Core;
using TouchSocket.Core.AspNetCore;
using TouchSocket.Http;
using TouchSocket.Sockets;

namespace MCServerLauncher.ProtocolTests;

[Collection(DaemonInstanceStorageIsolationCollection.Name)]
public class TouchSocketHostingCompositionTests
{
    [Fact]
    [Trait("Category", "DaemonInbound")]
    [Trait("Category", "DaemonInboundStatic")]
    public void DaemonTouchSocketTransportProfile_FileLocksExpectedConfigChainAndInternalSurface()
    {
        var source = ReadSourceFile("src/MCServerLauncher.Daemon/Bootstrap/DaemonTouchSocketTransportProfile.cs");

        Assert.Contains("internal static class DaemonTouchSocketTransportProfile", source, StringComparison.Ordinal);
        Assert.DoesNotContain("public static class DaemonTouchSocketTransportProfile", source, StringComparison.Ordinal);
        Assert.DoesNotContain("public class DaemonTouchSocketTransportProfile", source, StringComparison.Ordinal);

        var expectedOrder = new[]
        {
            "new IPHost(AppConfig.Get().Port)",
            "new AspNetCoreContainer(collection)",
            "SetListenIPHosts(listenHost)",
            "SetRegistrator(container)",
            "ConfigureContainer(a => DaemonServiceComposition.ConfigureContainer(",
            "ConfigurePlugins(a => DaemonServiceComposition.ConfigurePlugins(a, legacyEventQueueControl))",
        };

        var positions = expectedOrder
            .Select(marker => source.IndexOf(marker, StringComparison.Ordinal))
            .ToArray();

        for (var i = 0; i < expectedOrder.Length; i++)
        {
            Assert.True(positions[i] >= 0, $"Expected marker '{expectedOrder[i]}' not found in transport profile source.");
        }

        for (var i = 1; i < expectedOrder.Length; i++)
        {
            Assert.True(positions[i] > positions[i - 1], $"Marker '{expectedOrder[i]}' must appear after '{expectedOrder[i - 1]}' in transport profile source.");
        }
    }

    [Fact]
    [Trait("Category", "DaemonInbound")]
    [Trait("Category", "DaemonInboundStatic")]
    public void Application_SetupAsync_UsesTransportProfileHelperWithoutInlineTouchSocketConfigChain()
    {
        var source = ReadSourceFile("src/MCServerLauncher.Daemon/Application.cs");

        Assert.Contains("DaemonTouchSocketTransportProfile.CreateConfig", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new TouchSocketConfig()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("UseAspNetCoreContainer(collection)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ConfigurePlugins(a => DaemonServiceComposition.ConfigurePlugins(a))", source, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "DaemonInbound")]
    [Trait("Category", "DaemonInboundStatic")]
    public void Application_ServeAsync_LogsBindEndpointSeparatelyFromConnectableUrls()
    {
        var source = ReadSourceFile("src/MCServerLauncher.Daemon/Application.cs");

        Assert.Contains("\"[Remote] Bind endpoint: {BindEndpoint}\"", source, StringComparison.Ordinal);
        Assert.Contains("\"[Remote] Ws V2 connect URLs: {ConnectUrls}\"", source, StringComparison.Ordinal);
        Assert.Contains("\"[Remote] Ws V1 migration URLs: {ConnectUrls}\"", source, StringComparison.Ordinal);
        Assert.Contains("GetConnectableAuthorities", source, StringComparison.Ordinal);
        Assert.Contains("IPAddress.Loopback", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ws://{RemoteAddress}/api/v1", source, StringComparison.Ordinal);
        Assert.DoesNotContain("http://{RemoteAddress}/", source, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "DaemonInbound")]
    [Trait("Category", "DaemonInboundStatic")]
    public void Application_Shutdown_DelegatesToTheInternalApplicationLifecyclePort()
    {
        var source = ReadSourceFile("src/MCServerLauncher.Daemon/Application.cs");

        Assert.Contains("GetRequiredService<IDaemonRuntimeLifecycle>()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetRequiredService<IInstanceManager>()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetRequiredService<FileSessionCoordinator>()", source, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "DaemonInbound")]
    [Trait("Category", "DaemonInboundStatic")]
    public void DaemonAssembly_DoesNotExportTransportProfileHelper()
    {
        var exportedTypeNames = typeof(Application).Assembly
            .GetExportedTypes()
            .Select(type => type.FullName ?? type.Name)
            .ToArray();

        Assert.DoesNotContain(exportedTypeNames, name => name.Contains("DaemonTouchSocketTransportProfile", StringComparison.Ordinal));
        Assert.DoesNotContain(exportedTypeNames, name => name.Contains("TransportProfile", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Category", "DaemonInbound")]
    public async Task DaemonServiceComposition_ResolvesOneSharedApplicationGraph()
    {
        Directory.CreateDirectory(FileManager.InstancesRoot);
        var services = new ServiceCollection();
        services.AddLogging();
        var httpService = new HttpService();
        var rootContainer = new AspNetCoreContainer(services);
        var selectedRegistry = ActionHandlerRegistryRuntime.CreateSelected(useGeneratedActionRegistry: true);
        var config = new TouchSocketConfig()
            .SetRegistrator(rootContainer)
            .ConfigureContainer(registrator => DaemonServiceComposition.ConfigureContainer(
                registrator,
                services,
                httpService,
                selectedRegistry));
        IServiceProvider? rootProvider = null;
        DaemonLifecycleAttachment? attachment = null;
        try
        {
            await httpService.SetupAsync(config);
            var resolver = httpService.Resolver;
            rootProvider = rootContainer.ServiceProvider;
            var application = resolver.GetRequiredService<IDaemonApplication>();
            Assert.Same(resolver.GetRequiredService<IInstanceApplication>(), application.Instances);
            Assert.Same(resolver.GetRequiredService<IFileApplication>(), application.Files);
            Assert.Same(resolver.GetRequiredService<ISystemApplication>(), application.System);
            Assert.Same(resolver.GetRequiredService<IEventRuleApplication>(), application.EventRules);
            Assert.Same(FileSessionCoordinator.Shared, resolver.GetRequiredService<FileSessionCoordinator>());
            Assert.NotNull(resolver.GetRequiredService<IDaemonRuntimeLifecycle>());

            var catalogAccessor = resolver.GetRequiredService<IFrozenProtocolCatalogAccessor>();
            Assert.False(catalogAccessor.TryGet(out _));
            Assert.NotNull(resolver.GetRequiredService<TouchSocketV2TransportPlugin>());

            attachment = DaemonServiceComposition.AttachDaemonLifecycle(httpService);

            Assert.True(catalogAccessor.TryGet(out var attachedCatalog));
            var catalogComposition = resolver.GetRequiredService<BuiltInProtocolCatalogComposition>();
            Assert.Same(catalogComposition, resolver.GetRequiredService<BuiltInProtocolCatalogComposition>());
            Assert.Same(catalogComposition.Catalog, attachedCatalog);
            Assert.Same(catalogComposition.Catalog, catalogAccessor.GetRequired());
            Assert.Same(
                resolver.GetRequiredService<FrozenProtocolCatalogAccessor>(),
                catalogAccessor);
            Assert.Equal(BuiltInProtocolDefinitions.Rpcs.Length, catalogComposition.Catalog.Rpcs.Count);
            Assert.Equal(BuiltInProtocolDefinitions.Events.Length, catalogComposition.Catalog.Events.Count);
            Assert.Same(
                resolver.GetRequiredService<V2EventConnectionRegistry>(),
                resolver.GetRequiredService<V2EventConnectionRegistry>());
            Assert.NotNull(resolver.GetRequiredService<IV2RpcDiagnosticSink>());
            Assert.NotNull(resolver.GetRequiredService<IV2InboundDiagnosticSink>());
            Assert.NotNull(resolver.GetRequiredService<V2RemoteEventBridge>());

            var manager = resolver.GetRequiredService<InstanceManager>();
            Assert.Same(manager.InstanceSnapshotSource, resolver.GetRequiredService<IInstanceSnapshotSource>());

            var trigger = resolver.GetRequiredService<EventTriggerService>();
            var legacyAdapter = resolver.GetRequiredService<LegacyDomainEventAdapter>();
            var eventBridge = resolver.GetRequiredService<InstanceDomainEventBridge>();
            trigger.Stop();
            legacyAdapter.Stop();
            eventBridge.Dispose();
        }
        finally
        {
            await DisposeTouchSocketHostAsync(httpService, attachment, rootProvider);
        }
    }

    [Fact]
    [Trait("Category", "DaemonInbound")]
    public async Task DaemonLifecycleAttachment_StopBeforeStartPreventsLaterStartup()
    {
        var startCoreCount = 0;
        var stopCoreCount = 0;
        var attachment = new DaemonLifecycleAttachment(
            () =>
            {
                Interlocked.Increment(ref startCoreCount);
                return Task.CompletedTask;
            },
            () =>
            {
                Interlocked.Increment(ref stopCoreCount);
                return Task.CompletedTask;
            });

        await Task.WhenAll(attachment.StopAsync(), attachment.StopAsync());
        Assert.False(await attachment.StartAsync());

        Assert.Equal(0, startCoreCount);
        Assert.Equal(1, stopCoreCount);
        Assert.Equal(0, attachment.StartCount);
        Assert.Equal(1, attachment.StopCount);
        Assert.Equal(DaemonLifecycleState.Stopped, attachment.State);

        await attachment.DisposeAsync();
        await attachment.DisposeAsync();
        Assert.Equal(DaemonLifecycleState.Disposed, attachment.State);
        Assert.Equal(0, startCoreCount);
        Assert.Equal(1, stopCoreCount);
        Assert.Equal(0, attachment.StartCount);
        Assert.Equal(1, attachment.StopCount);
    }

    [Fact]
    [Trait("Category", "DaemonInbound")]
    public async Task DaemonLifecycleAttachment_ConcurrentStopWaitsForOneStartupThenStopsOnce()
    {
        var startEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseStart = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var startCoreCount = 0;
        var stopCoreCount = 0;
        var attachment = new DaemonLifecycleAttachment(
            async () =>
            {
                Interlocked.Increment(ref startCoreCount);
                startEntered.TrySetResult();
                await releaseStart.Task;
            },
            () =>
            {
                Interlocked.Increment(ref stopCoreCount);
                return Task.CompletedTask;
            });

        var startTask = attachment.StartAsync();
        await startEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var stopTask = attachment.StopAsync();
        Assert.False(stopTask.IsCompleted);

        releaseStart.TrySetResult();
        Assert.True(await startTask);
        await Task.WhenAll(stopTask, attachment.StopAsync());

        Assert.Equal(1, startCoreCount);
        Assert.Equal(1, stopCoreCount);
        Assert.Equal(1, attachment.StartCount);
        Assert.Equal(1, attachment.StopCount);
        Assert.Equal(DaemonLifecycleState.Stopped, attachment.State);
        Assert.False(await attachment.StartAsync());

        await attachment.DisposeAsync();
    }

    [Fact]
    [Trait("Category", "DaemonInbound")]
    public async Task Application_PrecompletedShutdownStopsWithoutStartingAndDisposesRootProvider()
    {
        await Application.DisposeCurrentHostAsync();
        ActionHandlerRegistryRuntime.Initialize(useGeneratedActionRegistry: true);
        var coreStopCount = 0;
        var stoppingCount = 0;
        Func<Task> stoppingHandler = () =>
        {
            Interlocked.Increment(ref stoppingCount);
            return Task.CompletedTask;
        };
        Application.OnStopping += stoppingHandler;
        AsyncDisposeProbe? probe = null;
        Application.DaemonHostContext? host = null;
        try
        {
            await Application.SetupAsync(
                services => services.AddSingleton<AsyncDisposeProbe>(),
                new IPHost(IPAddress.Loopback, 0),
                new Application.DaemonHostTestHooks(StopCore: _ =>
                {
                    Interlocked.Increment(ref coreStopCount);
                    return Task.CompletedTask;
                }));
            host = Assert.IsType<Application.DaemonHostContext>(Application.CurrentHostContext);
            Assert.Same(host.RootContainer.ServiceProvider, host.RootProvider);
            Assert.Same(host.HttpService, Application.HttpService);
            probe = host.RootProvider.GetRequiredService<AsyncDisposeProbe>();
            var shutdown = host.RootProvider.GetRequiredService<GracefulShutdown>();

            await shutdown.Shutdown();
            await Application.ServeAsync();

            Assert.Equal(0, host.Lifecycle.StartCount);
            Assert.Equal(1, host.Lifecycle.StopCount);
            Assert.Equal(DaemonLifecycleState.Disposed, host.Lifecycle.State);
            Assert.Equal(1, host.StopExecutionCount);
            Assert.Equal(1, host.DisposeExecutionCount);
            Assert.Equal(1, stoppingCount);
            Assert.Equal(1, coreStopCount);
            Assert.Equal(1, probe.DisposeCount);
            Assert.Null(Application.CurrentHostContext);
        }
        finally
        {
            try
            {
                Application.OnStopping -= stoppingHandler;
                if (Application.CurrentHostContext is { IsServing: false })
                    await Application.DisposeCurrentHostAsync();
            }
            finally
            {
                ActionHandlerRegistryRuntime.Reset();
            }
        }
    }

    [Fact]
    [Trait("Category", "DaemonInbound")]
    public async Task Application_HttpServiceDisposeFailureIsReportedAfterRemainingCleanup()
    {
        await Application.DisposeCurrentHostAsync();
        ActionHandlerRegistryRuntime.Initialize(useGeneratedActionRegistry: true);
        const string resultMessage = "controlled-sensitive-normal-dispose-detail";
        var coreStopCount = 0;
        var disposeAttemptCount = 0;
        AsyncDisposeProbe? probe = null;
        Application.DaemonHostContext? host = null;
        try
        {
            await Application.SetupAsync(
                services => services.AddSingleton<AsyncDisposeProbe>(),
                new IPHost(IPAddress.Loopback, 0),
                new Application.DaemonHostTestHooks(
                    StopCore: _ =>
                    {
                        Interlocked.Increment(ref coreStopCount);
                        return Task.CompletedTask;
                    },
                    DisposeHttpService: service =>
                    {
                        Interlocked.Increment(ref disposeAttemptCount);
                        Assert.True(service.SafeDispose().IsSuccess);
                        return Result.FromError(resultMessage);
                    }));
            host = Assert.IsType<Application.DaemonHostContext>(Application.CurrentHostContext);
            probe = host.RootProvider.GetRequiredService<AsyncDisposeProbe>();
            await host.RootProvider.GetRequiredService<GracefulShutdown>().Shutdown();

            var failure = await Assert.ThrowsAsync<AggregateException>(() => Application.ServeAsync());
            AssertHttpServiceDisposeFailure(failure, resultMessage);

            Assert.Equal(1, disposeAttemptCount);
            Assert.Equal(1, coreStopCount);
            Assert.Equal(1, host.Lifecycle.StopCount);
            Assert.Equal(DaemonLifecycleState.Disposed, host.Lifecycle.State);
            Assert.Equal(1, host.DisposeExecutionCount);
            Assert.Equal(1, probe.DisposeCount);
            Assert.Null(Application.CurrentHostContext);
        }
        finally
        {
            try
            {
                if (Application.CurrentHostContext is { IsServing: false })
                    await Application.DisposeCurrentHostAsync();
            }
            finally
            {
                ActionHandlerRegistryRuntime.Reset();
            }
        }
    }

    [Fact]
    [Trait("Category", "DaemonInbound")]
    public async Task Application_ShutdownAfterListenerStartCannotStartStoppedLifecycle()
    {
        await Application.DisposeCurrentHostAsync();
        ActionHandlerRegistryRuntime.Initialize(useGeneratedActionRegistry: true);
        var startedCount = 0;
        var stoppingCount = 0;
        var coreStopCount = 0;
        Func<Task> startedHandler = () =>
        {
            Interlocked.Increment(ref startedCount);
            return Task.CompletedTask;
        };
        Func<Task> stoppingHandler = () =>
        {
            Interlocked.Increment(ref stoppingCount);
            return Task.CompletedTask;
        };
        Application.OnStarted += startedHandler;
        Application.OnStopping += stoppingHandler;
        Application.DaemonHostContext? host = null;
        try
        {
            await Application.SetupAsync(
                null,
                new IPHost(IPAddress.Loopback, 0),
                new Application.DaemonHostTestHooks(StopCore: _ =>
                {
                    Interlocked.Increment(ref coreStopCount);
                    return Task.CompletedTask;
                }));
            host = Assert.IsType<Application.DaemonHostContext>(Application.CurrentHostContext);
            var shutdown = host.RootProvider.GetRequiredService<GracefulShutdown>();

            await Application.ServeAsync(async localHost =>
            {
                Assert.Same(host, localHost);
                await shutdown.Shutdown();
            });

            Assert.Equal(0, startedCount);
            Assert.Equal(1, stoppingCount);
            Assert.Equal(0, host.Lifecycle.StartCount);
            Assert.Equal(1, host.Lifecycle.StopCount);
            Assert.Equal(DaemonLifecycleState.Disposed, host.Lifecycle.State);
            Assert.Equal(1, host.StopExecutionCount);
            Assert.Equal(1, coreStopCount);
            Assert.Null(Application.CurrentHostContext);
        }
        finally
        {
            try
            {
                Application.OnStarted -= startedHandler;
                Application.OnStopping -= stoppingHandler;
                if (Application.CurrentHostContext is { IsServing: false })
                    await Application.DisposeCurrentHostAsync();
            }
            finally
            {
                ActionHandlerRegistryRuntime.Reset();
            }
        }
    }

    [Fact]
    [Trait("Category", "DaemonInbound")]
    public async Task Application_RejectsHostReplacementWhileServeOwnsCapturedContext()
    {
        await Application.DisposeCurrentHostAsync();
        ActionHandlerRegistryRuntime.Initialize(useGeneratedActionRegistry: true);
        var listenerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseListener = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var configureReplacementCount = 0;
        var coreStopCount = 0;
        Application.DaemonHostContext? host = null;
        Task? serveTask = null;
        try
        {
            await Application.SetupAsync(
                services => services.AddSingleton<AsyncDisposeProbe>(),
                new IPHost(IPAddress.Loopback, 0),
                new Application.DaemonHostTestHooks(StopCore: _ =>
                {
                    Interlocked.Increment(ref coreStopCount);
                    return Task.CompletedTask;
                }));
            host = Assert.IsType<Application.DaemonHostContext>(Application.CurrentHostContext);
            var probe = host.RootProvider.GetRequiredService<AsyncDisposeProbe>();
            var shutdown = host.RootProvider.GetRequiredService<GracefulShutdown>();
            serveTask = Application.ServeAsync(async localHost =>
            {
                Assert.Same(host, localHost);
                Assert.Same(host.HttpService, Application.HttpService);
                listenerStarted.TrySetResult();
                await releaseListener.Task;
            });

            await listenerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var replacement = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                Application.SetupAsync(services =>
                {
                    Interlocked.Increment(ref configureReplacementCount);
                    services.AddSingleton<AsyncDisposeProbe>();
                }));
            Assert.Contains("cannot be replaced", replacement.Message, StringComparison.Ordinal);
            Assert.Equal(0, configureReplacementCount);
            Assert.Same(host, Application.CurrentHostContext);
            Assert.Same(host.HttpService, Application.HttpService);

            var shutdownTask = shutdown.Shutdown();
            releaseListener.TrySetResult();
            await Task.WhenAll(serveTask, shutdownTask).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(1, host.StopExecutionCount);
            Assert.Equal(1, host.DisposeExecutionCount);
            Assert.Equal(1, coreStopCount);
            Assert.Equal(1, probe.DisposeCount);
            Assert.Null(Application.CurrentHostContext);
        }
        finally
        {
            try
            {
                releaseListener.TrySetResult();
                if (serveTask is not null)
                {
                    var current = Application.CurrentHostContext;
                    if (current is not null)
                        _ = current.RootProvider.GetRequiredService<GracefulShutdown>().Shutdown();
                    await serveTask.WaitAsync(TimeSpan.FromSeconds(10));
                }
                else if (Application.CurrentHostContext is { IsServing: false })
                {
                    await Application.DisposeCurrentHostAsync();
                }
            }
            finally
            {
                ActionHandlerRegistryRuntime.Reset();
            }
        }
    }

    [Fact]
    [Trait("Category", "DaemonInbound")]
    public async Task Application_ReplacingUnservedHostDisposesEachRootProviderExactlyOnce()
    {
        await Application.DisposeCurrentHostAsync();
        ActionHandlerRegistryRuntime.Initialize(useGeneratedActionRegistry: true);
        Application.DaemonHostContext? firstHost = null;
        AsyncDisposeProbe? firstProbe = null;
        AsyncDisposeProbe? secondProbe = null;
        try
        {
            await Application.SetupAsync(services => services.AddSingleton<AsyncDisposeProbe>());
            firstHost = Assert.IsType<Application.DaemonHostContext>(Application.CurrentHostContext);
            firstProbe = firstHost.RootProvider.GetRequiredService<AsyncDisposeProbe>();

            await Application.SetupAsync(services => services.AddSingleton<AsyncDisposeProbe>());
            var secondHost = Assert.IsType<Application.DaemonHostContext>(Application.CurrentHostContext);
            secondProbe = secondHost.RootProvider.GetRequiredService<AsyncDisposeProbe>();

            Assert.Equal(1, firstHost.DisposeExecutionCount);
            Assert.Equal(1, firstProbe.DisposeCount);
            Assert.Equal(0, secondProbe.DisposeCount);

            await Application.DisposeCurrentHostAsync();
            await firstHost.DisposeAsync();

            Assert.Equal(1, firstHost.DisposeExecutionCount);
            Assert.Equal(1, firstProbe.DisposeCount);
            Assert.Equal(1, secondHost.DisposeExecutionCount);
            Assert.Equal(1, secondProbe.DisposeCount);
            Assert.Null(Application.CurrentHostContext);
        }
        finally
        {
            try
            {
                if (Application.CurrentHostContext is { IsServing: false })
                    await Application.DisposeCurrentHostAsync();
            }
            finally
            {
                ActionHandlerRegistryRuntime.Reset();
            }
        }
    }

    [Fact]
    [Trait("Category", "DaemonInbound")]
    public async Task Application_SetupFailureDisposesPartiallyBuiltRootProvider()
    {
        await Application.DisposeCurrentHostAsync();
        ActionHandlerRegistryRuntime.Initialize(useGeneratedActionRegistry: true);
        const string resultMessage = "controlled-sensitive-rollback-dispose-detail";
        AsyncDisposeProbe? probe = null;
        var disposeAttemptCount = 0;
        try
        {
            var failure = await Assert.ThrowsAnyAsync<Exception>(() => Application.SetupAsync(
                services =>
                {
                    services.AddSingleton<AsyncDisposeProbe>();
                    services.AddSingleton<ILogger<V2RpcLoggingDiagnosticSink>>(provider =>
                    {
                        probe = provider.GetRequiredService<AsyncDisposeProbe>();
                        throw new InvalidOperationException("Controlled provider-build failure.");
                    });
                },
                null,
                new Application.DaemonHostTestHooks(DisposeHttpService: service =>
                {
                    Interlocked.Increment(ref disposeAttemptCount);
                    Assert.True(service.SafeDispose().IsSuccess);
                    return Result.FromError(resultMessage);
                })));

            AssertHttpServiceDisposeFailure(failure, resultMessage);
            Assert.NotNull(probe);
            Assert.Equal(1, disposeAttemptCount);
            Assert.Equal(1, probe.DisposeCount);
            Assert.Null(Application.CurrentHostContext);
        }
        finally
        {
            try
            {
                if (Application.CurrentHostContext is { IsServing: false })
                    await Application.DisposeCurrentHostAsync();
            }
            finally
            {
                ActionHandlerRegistryRuntime.Reset();
            }
        }
    }

    [Fact]
    [Trait("Category", "DaemonInbound")]
    [Trait("Category", "DaemonInboundStatic")]
    public void DaemonServiceComposition_ConfiguresTheSharedFileCoordinatorBeforeStartup()
    {
        var source = ReadSourceFile("src/MCServerLauncher.Daemon/Bootstrap/DaemonServiceComposition.cs");
        var acquire = source.IndexOf("var fileSessionCoordinator = FileSessionCoordinator.Shared", StringComparison.Ordinal);
        var configure = source.IndexOf(
            "fileSessionCoordinator.ConfigureDownloadSessionLimit(AppConfig.Get().FileDownloadSessions)",
            StringComparison.Ordinal);
        var register = source.IndexOf("a.RegisterSingleton(fileSessionCoordinator)", StringComparison.Ordinal);
        var start = source.IndexOf("files.Start()", StringComparison.Ordinal);

        Assert.True(acquire >= 0);
        Assert.True(configure > acquire);
        Assert.True(register > configure);
        Assert.True(start > register);
    }

    [Fact]
    [Trait("Category", "DaemonInbound")]
    [Trait("Category", "DaemonInboundStatic")]
    public void Application_AttachesTheFrozenCatalogAfterSetupAndBeforeStartingTheListener()
    {
        var source = ReadSourceFile("src/MCServerLauncher.Daemon/Application.cs");
        var createMethod = source.IndexOf("private static async Task<DaemonHostContext> CreateHostAsync", StringComparison.Ordinal);
        var setup = source.IndexOf("await httpService.SetupAsync(transport.Config)", createMethod, StringComparison.Ordinal);
        var attach = source.IndexOf("DaemonServiceComposition.AttachDaemonLifecycle(httpService)", createMethod, StringComparison.Ordinal);
        var serveMethod = source.IndexOf("internal static async Task ServeAsync", StringComparison.Ordinal);
        var start = source.IndexOf("await host.HttpService.StartAsync()", serveMethod, StringComparison.Ordinal);

        Assert.True(createMethod >= 0);
        Assert.True(setup > createMethod);
        Assert.True(attach > setup);
        Assert.True(serveMethod >= 0);
        Assert.True(start > serveMethod);
        Assert.Equal(attach, source.LastIndexOf("DaemonServiceComposition.AttachDaemonLifecycle(httpService)", StringComparison.Ordinal));
    }

    private static void AssertHttpServiceDisposeFailure(Exception exception, string expectedMessage)
    {
        var failures = exception is AggregateException aggregate
            ? aggregate.Flatten().InnerExceptions
            : [exception];

        var disposalFailure = Assert.Single(
            failures.OfType<Application.HttpServiceDisposeException>());

        Assert.Equal(ResultCode.Error, disposalFailure.ResultCode);
        Assert.Equal(expectedMessage, disposalFailure.ResultMessage);
        Assert.DoesNotContain(expectedMessage, exception.ToString(), StringComparison.Ordinal);
    }

    private static async Task DisposeTouchSocketHostAsync(
        HttpService httpService,
        DaemonLifecycleAttachment? attachment,
        IServiceProvider? rootProvider)
    {
        if (attachment is not null)
            await attachment.DisposeAsync();
        await httpService.StopAsync();
        httpService.SafeDispose();

        if (rootProvider is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync();
        else if (rootProvider is IDisposable disposable)
            disposable.Dispose();
    }

    private sealed class AsyncDisposeProbe : IAsyncDisposable
    {
        private int _disposeCount;

        public AsyncDisposeProbe()
        {
        }

        internal int DisposeCount => Volatile.Read(ref _disposeCount);

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCount);
            return ValueTask.CompletedTask;
        }
    }

    private static string ReadSourceFile(string relativePath)
    {
        var repoRoot = ResolveRepoRoot();
        var fullPath = Path.Combine(repoRoot, relativePath);

        Assert.True(File.Exists(fullPath), $"Source file not found: {relativePath}");
        return File.ReadAllText(fullPath);
    }

    private static string ResolveRepoRoot()
    {
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "MCServerLauncher.sln")))
        {
            dir = Directory.GetParent(dir)?.FullName;
        }

        return dir ?? throw new DirectoryNotFoundException("Repository root not found");
    }
}
