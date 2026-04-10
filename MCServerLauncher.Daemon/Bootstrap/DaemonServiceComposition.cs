using MCServerLauncher.Common.Helpers;
using MCServerLauncher.Common.ProtoType;
using MCServerLauncher.Common.ProtoType.Status;
using MCServerLauncher.Daemon.Console;
using MCServerLauncher.Daemon.Management;
using MCServerLauncher.Daemon.Remote;
using MCServerLauncher.Daemon.Remote.Action;
using MCServerLauncher.Daemon.Remote.Event;
using MCServerLauncher.Daemon.Storage;
using MCServerLauncher.Daemon.Utils.LazyCell;
using MCServerLauncher.Daemon.Utils.Status;
using Microsoft.Extensions.DependencyInjection;
using TouchSocket.Core;
using TouchSocket.Http;
using Timer = System.Timers.Timer;

namespace MCServerLauncher.Daemon.Bootstrap;

internal static class DaemonServiceComposition
{
    internal static void ConfigureContainer(
        IRegistrator a,
        IServiceCollection collection,
        HttpService httpService,
        ActionHandlerRegistrySnapshot selectedRegistry)
    {
        a.RegisterSingleton<IServiceCollection>(collection);
        a.RegisterSingleton<ConsoleApplication>();
        a.RegisterSingleton<GracefulShutdown>();
        a.RegisterSingleton<IHttpService>(httpService);
        a.RegisterSingleton(selectedRegistry);
        a.RegisterSingleton<IActionExecutor, AnotherActionExecutor>();
        a.RegisterSingleton<IEventService, EventService>();
        a.RegisterSingleton<WsContextContainer>();
        a.RegisterSingleton(InstanceManager.Create());
        a.RegisterSingleton<EventTriggerService>();
        a.RegisterSingleton<IAsyncTimedLazyCell<SystemInfo>>(
            new AsyncTimedLazyCell<SystemInfo>(
                SystemInfoHelper.GetSystemInfo,
                TimeSpan.FromSeconds(2)
            )
        );
        a.RegisterSingleton<IAsyncTimedLazyCell<JavaInfo[]>>(
            new AsyncTimedLazyCell<JavaInfo[]>(
                JavaScanner.ScanJavaAsync,
                TimeSpan.FromSeconds(2)
            )
        );
    }

    internal static void ConfigurePlugins(IPluginManager a)
    {
        a.Add<FileSystemWatcherPlugin>();

        a.Add<HttpPlugin>();
        a.UseWebSocket(options =>
        {
            options.SetUrl("/api/v1");
            options.SetVerifyConnection(WsVerifyHandler.VerifyHandler);
            options.SetAutoPong(true);
        });

        a.Add<WsBasePlugin>();
        a.Add<WsActionPlugin>();
        a.Add<WsEventPlugin>();
        a.Add<WsExpirationPlugin>(); // WsExpirePlugin注册必须在WsBasePlugin之后
        a.UseDefaultHttpServicePlugin();
    }

    internal static void AttachDaemonLifecycle(HttpService httpService)
    {
        var daemonReportTimer = new Timer(3000);
        daemonReportTimer.AutoReset = true;
        daemonReportTimer.Elapsed += async (sender, args) =>
        {
            var eventService = httpService.Resolver.GetRequiredService<IEventService>();
            var cell = httpService.Resolver.GetRequiredService<IAsyncTimedLazyCell<SystemInfo>>();
            var (osInfo, cpuInfo, memInfo, driveInformation) = await cell.Value;
            eventService.OnDaemonReport(new DaemonReport(
                osInfo,
                cpuInfo,
                memInfo,
                driveInformation,
                Application.StartTime.ToUnixTimeMilliSeconds()
            ));
        };
        Application.OnStarted += () =>
        {
            daemonReportTimer.Start();
            httpService.Resolver.GetRequiredService<ConsoleApplication>().Serve();
            return Task.CompletedTask;
        };
        Application.OnStopping += () =>
        {
            daemonReportTimer.Stop();
            return Task.CompletedTask;
        };
    }
}
