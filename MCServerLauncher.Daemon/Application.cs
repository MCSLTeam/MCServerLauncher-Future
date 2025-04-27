using System.Reflection;
using MCServerLauncher.Common.Helpers;
using MCServerLauncher.Common.ProtoType;
using MCServerLauncher.Common.ProtoType.Status;
using MCServerLauncher.Daemon.Console;
using MCServerLauncher.Daemon.Minecraft.Server;
using MCServerLauncher.Daemon.Minecraft.Server.Factory;
using MCServerLauncher.Daemon.Remote;
using MCServerLauncher.Daemon.Remote.Action;
using MCServerLauncher.Daemon.Remote.Event;
using MCServerLauncher.Daemon.Remote.Event.Extensions;
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

public class Application
{
    public static readonly DateTime StartTime = DateTime.Now;

    private readonly Timer _daemonReportTimer;
    private readonly HttpService _httpService;

    public Application()
    {
        IServiceCollection collection = new ServiceCollection();

        _httpService = new HttpService();
        _httpService.Setup(new TouchSocketConfig()
            .SetListenIPHosts(AppConfig.Get().Port)
            .UseAspNetCoreContainer(collection)
            .ConfigureContainer(a =>
            {
                a.RegisterSingleton<IServiceCollection>(collection)
                    .RegisterSingleton<ConsoleApplication>()
                    .RegisterSingleton<GracefulShutdown>()
                    .RegisterSingleton<IHttpService>(_httpService)
                    .RegisterSingleton<IActionService, ActionService>()
                    .RegisterSingleton<IEventService, EventService>()
                    .RegisterSingleton<WsContextContainer>()
                    .RegisterSingleton<ActionHandlerRegistry>()
                    .RegisterSingleton<IInstanceManager>(InstanceManager.Create())
                    .RegisterSingleton<IAsyncTimedLazyCell<SystemInfo>>(
                        new AsyncTimedLazyCell<SystemInfo>(
                            SystemInfoHelper.GetSystemInfo,
                            TimeSpan.FromSeconds(2)
                        )
                    )
                    .RegisterSingleton<IAsyncTimedLazyCell<JavaInfo[]>>(
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
                a.UseWebSocket()
                    .SetWSUrl("/api/v1")
                    .SetVerifyConnection(WsVerifyHandler.VerifyHandler)
                    .UseAutoPong();

                a.Add<WsBasePlugin>();
                a.Add<WsActionPlugin>();
                a.Add<WsEventPlugin>();
                a.Add<WsExpirationPlugin>(); // WsExpirePlugin注册必须在WsBasePlugin之后
                a.UseDefaultHttpServicePlugin();
            })
        );
        PostApplicationContainerBuilt(resolver =>
        {
            resolver.GetRequiredService<ActionHandlerRegistry>().RegisterHandlers();
            resolver.GetRequiredService<ConsoleApplication>().Serve();
        });

        _daemonReportTimer = new Timer(3000);
        _daemonReportTimer.AutoReset = true;
        _daemonReportTimer.Elapsed += async (sender, args) =>
        {
            var eventService = _httpService.Resolver.GetRequiredService<IEventService>();
            var cell = _httpService.Resolver.GetRequiredService<IAsyncTimedLazyCell<SystemInfo>>();
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

    public static Version AppVersion => Assembly.GetExecutingAssembly().GetName().Version!;

    /// <summary>
    ///     读取配置,添加/api/v1和/login路由的handler,并启动HttpServer。
    ///     路由/api/v1: ws长连接，实现rpc。
    ///     路由/login: http请求，实现登录，返回一个jwt。
    /// </summary>
    public async Task ServeAsync()
    {
        var config = AppConfig.Get();
        var resolver = _httpService.Resolver;
        var gs = resolver.GetRequiredService<GracefulShutdown>();

        await _httpService.StartAsync();
        Log.Information("[Remote] Ws Server started at ws://0.0.0.0:{0}/api/v1", config.Port);
        Log.Information("[Remote] Http Server started at http://0.0.0.0:{0}/", config.Port);
        _daemonReportTimer.Start();
        gs.OnShutdown += () => StopAsync().Wait();

        await gs.WaitForShutdownAsync();

        // 最后释放HttpService
        Log.Debug("[Application] shutting down Http service ...");
        await _httpService.StopAsync();
    }

    private async Task StopAsync(int timeout = -1)
    {
        _daemonReportTimer.Stop();

        var cts = new CancellationTokenSource();

        var manager = _httpService.Resolver.GetRequiredService<IInstanceManager>();

        cts.CancelAfter(timeout);

        Log.Debug("[InstanceManager] stopping instances ...");
        await manager.StopAllInstances(cts.Token);

        Log.Debug("[WsContextContainer] closing websocket connections ...");
        foreach (var id in _httpService.Resolver.GetRequiredService<WsContextContainer>().GetClientIds())
            await _httpService.GetClient(id).WebSocket.CloseAsync("Daemon exit", cts.Token);
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

    public static bool Init()
    {
        InitLogger();
        Log.Information("MCServerLauncher.Daemon v{0}", AppVersion);

        InitDataDirectory();
        ContainedFiles.ExtractContained();
        FileManager.StartFileSessionsWatcher();
        InstanceFactoryRegistry.LoadFactories();

        try
        {
            // windows下预先检查CIM是否可用
            SystemInfoHelper.GetSystemInfo().Wait();
            return true;
        }
        catch (AggregateException e)
        {
            Log.Error("Could not Init Application: {0}", e.Message);
            return false;
        }
    }

    private void PostApplicationContainerBuilt(Action<IResolver> setup)
    {
        setup.Invoke(_httpService.Resolver);
    }

    #endregion
}