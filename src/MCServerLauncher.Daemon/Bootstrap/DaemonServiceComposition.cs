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
using MCServerLauncher.Daemon.Storage;
using MCServerLauncher.Daemon.Utils.LazyCell;
using MCServerLauncher.Daemon.Utils.Status;
using MessagePipe;
using Microsoft.Extensions.DependencyInjection;
using TouchSocket.Core;
using TouchSocket.Http;
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
        a.RegisterSingleton<InstanceDomainEventBridge>();
        a.RegisterSingleton<EventTriggerService>();
        a.RegisterSingleton<LegacyDomainEventAdapter>();
        a.RegisterSingleton<DaemonReportPublisher>();
    }

    internal static void ConfigurePlugins(
        IPluginManager a,
        LegacyEventQueueControl? legacyEventQueueControl = null)
    {
        a.Add<HttpPlugin>();
        a.UseWebSocket(options =>
        {
            options.SetUrl("/api/v1");
            options.SetVerifyConnection(WsVerifyHandler.VerifyHandler);
            options.SetAutoPong(true);
        });

        a.Add<WsBasePlugin>();
        a.Add<WsActionPlugin>();
        var wsEventPlugin = a.Add<WsEventPlugin>();
        (legacyEventQueueControl ?? throw new InvalidOperationException(
            "Legacy event queue control must be provided when configuring plugins."))
            .Attach(wsEventPlugin);
        a.Add<WsExpirationPlugin>(); // WsExpirePlugin注册必须在WsBasePlugin之后
        a.UseDefaultHttpServicePlugin();
    }

    internal static void AttachDaemonLifecycle(HttpService httpService)
    {
        Application.OnStarted += () =>
        {
            httpService.Resolver.GetRequiredService<FileSessionCoordinator>().Start();
            _ = httpService.Resolver.GetRequiredService<EventTriggerService>();
            _ = httpService.Resolver.GetRequiredService<LegacyDomainEventAdapter>();
            _ = httpService.Resolver.GetRequiredService<InstanceDomainEventBridge>();
            httpService.Resolver.GetRequiredService<DaemonReportPublisher>().Start();
            httpService.Resolver.GetRequiredService<ConsoleApplication>().Serve();
            return Task.CompletedTask;
        };
        Application.OnStopping += () =>
        {
            httpService.Resolver.GetRequiredService<DaemonReportPublisher>().RequestStop();
            return Task.CompletedTask;
        };
    }
}
