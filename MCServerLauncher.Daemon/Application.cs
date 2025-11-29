using System.Reflection;
using MCServerLauncher.Common.Helpers;
using MCServerLauncher.Common.ProtoType;
using MCServerLauncher.Common.ProtoType.Status;
using MCServerLauncher.Daemon.Console;
using MCServerLauncher.Daemon.Management;
using MCServerLauncher.Daemon.Management.Factory;
using MCServerLauncher.Daemon.Remote;
using MCServerLauncher.Daemon.Remote.Action;
using MCServerLauncher.Daemon.Remote.Action.Handlers;
using MCServerLauncher.Daemon.Remote.Event;
using MCServerLauncher.Daemon.Storage;
using MCServerLauncher.Daemon.Utils.LazyCell;
using MCServerLauncher.Daemon.Utils.Status;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using TouchSocket.Core;
using TouchSocket.Http;
using TouchSocket.Sockets;
using Timer = System.Timers.Timer;

namespace MCServerLauncher.Daemon;

public static class Application
{
    public static readonly DateTime StartTime = DateTime.Now;
    private static Timer _daemonReportTimer;
    public static HttpService HttpService { get; private set; }
    public static Version AppVersion => Assembly.GetExecutingAssembly().GetName().Version!;

    public static async Task SetupAsync()
    {
        IServiceCollection collection = new ServiceCollection();

        HttpService = new HttpService();
        await HttpService.SetupAsync(new TouchSocketConfig()
            .SetListenIPHosts(AppConfig.Get().Port)
            .UseAspNetCoreContainer(collection)
            .ConfigureContainer(a =>
            {
                a.RegisterSingleton<IServiceCollection>(collection);
                a.RegisterSingleton<ConsoleApplication>();
                a.RegisterSingleton<GracefulShutdown>();
                a.RegisterSingleton<IHttpService>(HttpService);
                a.RegisterSingleton<IActionService, ActionService>();
                a.RegisterSingleton<IEventService, EventService>();
                a.RegisterSingleton<WsContextContainer>();
                a.RegisterSingleton<ActionHandlerRegistry>();
                a.RegisterSingleton<IInstanceManager>(InstanceManager.Create());
                a.RegisterSingleton<IAsyncTimedLazyCell<SystemInfo>>(
                    new AsyncTimedLazyCell<SystemInfo>(
                        SystemInfoHelper.GetSystemInfo,
                        TimeSpan.FromSeconds(2)
                    )
                );
                a.RegisterSingleton<IAsyncTimedLazyCell<JavaInfo[]>>(
                    new AsyncTimedLazyCell<JavaInfo[]>(
                        JavaScanner.ScanJavaAsync,
                        TimeSpan.FromMilliseconds(60000)
                    )
                );
            })
            .ConfigurePlugins(a =>
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
            })
        );

        HttpService.Resolver.GetRequiredService<ActionHandlerRegistry>().RegisterHandlers();
        HttpService.Resolver.GetRequiredService<ConsoleApplication>().Serve();

        _daemonReportTimer = new Timer(3000);
        _daemonReportTimer.AutoReset = true;
        _daemonReportTimer.Elapsed += async (sender, args) =>
        {
            var eventService = HttpService.Resolver.GetRequiredService<IEventService>();
            var cell = HttpService.Resolver.GetRequiredService<IAsyncTimedLazyCell<SystemInfo>>();
            var (osInfo, cpuInfo, memInfo, driveInformation) = await cell.Value;
            eventService.OnDaemonReport(new DaemonReport(
                osInfo,
                cpuInfo,
                memInfo,
                driveInformation,
                StartTime.ToUnixTimeMilliSeconds()
            ));
        };
    }


    /// <summary>
    ///     读取配置,添加/api/v1和/login路由的handler,并启动HttpServer。
    ///     路由/api/v1: ws长连接，实现rpc。
    ///     路由/login: http请求，实现登录，返回一个jwt。
    /// </summary>
    public static async Task ServeAsync()
    {
        var config = AppConfig.Get();
        var resolver = HttpService.Resolver;
        var gs = resolver.GetRequiredService<GracefulShutdown>();

        await HttpService.StartAsync();
        Log.Information("[Remote] Ws Server started at ws://0.0.0.0:{0}/api/v1", config.Port);
        Log.Information("[Remote] Http Server started at http://0.0.0.0:{0}/", config.Port);
        _daemonReportTimer.Start();
        gs.OnShutdown += () => StopAsync().Wait();

        await gs.WaitForShutdownAsync();

        // 最后释放HttpService
        Log.Debug("[Application] shutting down Http service ...");
        await HttpService.StopAsync();
    }

    private static async Task StopAsync(int timeout = -1)
    {
        _daemonReportTimer.Stop();

        var cts = new CancellationTokenSource();

        var manager = HttpService.Resolver.GetRequiredService<IInstanceManager>();

        cts.CancelAfter(timeout);

        Log.Debug("[InstanceManager] stopping instances ...");
        await manager.StopAllInstances(cts.Token);

        Log.Debug("[WsContextContainer] closing websocket connections ...");
        foreach (var id in HttpService.Resolver.GetRequiredService<WsContextContainer>().GetClientIds())
            await HttpService.GetClient(id).WebSocket.CloseAsync("Daemon exit", cts.Token);
    }

    #region Init

    private static void InitDataDirectory()
    {
        var dataFolders = new List<string>
        {
            FileManager.Root,
            FileManager.InstancesRoot,
            FileManager.LogRoot,
            FileManager.ContainedRoot
        };

        foreach (var dataFolder in dataFolders.Where(dataFolder => !Directory.Exists(dataFolder)))
            Directory.CreateDirectory(dataFolder!);
    }

    private static void InitLogger()
    {
        var logConfig = new LoggerConfiguration();

        logConfig = AppConfig.Get().Verbose ? logConfig.MinimumLevel.Verbose() : logConfig.MinimumLevel.Information();

        Log.Logger = logConfig
            .WriteTo.Async(a => a.File($"{FileManager.LogRoot}/daemon-.txt", rollingInterval: RollingInterval.Day,
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"))
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }

    public static async Task<bool> InitAsync()
    {
        InitLogger();
        Log.Information("MCServerLauncher.Daemon v{0}", AppVersion);

        InitDataDirectory();
        ContainedFiles.ExtractContained();
        FileManager.StartFileSessionsWatcher();

        try
        {
            // windows下预先检查CIM是否可用
            await SystemInfoHelper.GetSystemInfo();
        }
        catch (AggregateException e)
        {
            Log.Error("Could not Init Application: {0}",
                string.Join("\n", e.InnerExceptions.Select(x => x.ToString())));
            return false;
        }

        foreach (var type in Assembly.GetExecutingAssembly().GetTypes())
        {
            InstanceFactoryRegistry.LoadFactoryFromType(type);
            AnotherActionHandlerRegistry.LoadHandlerFromType(type);
        }

        return true;
    }

    #endregion
}