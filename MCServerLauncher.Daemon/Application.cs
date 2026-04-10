using System.Reflection;
using MCServerLauncher.Daemon.Bootstrap;
using MCServerLauncher.Daemon.Management;
using MCServerLauncher.Daemon.Remote;
using MCServerLauncher.Daemon.Remote.Action;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using TouchSocket.Core;
using TouchSocket.Http;

namespace MCServerLauncher.Daemon;

public static class Application
{
    public static readonly DateTime StartTime = DateTime.Now;
    // Timer lifecycle managed by DaemonServiceComposition.AttachDaemonLifecycle
    public static HttpService HttpService { get; private set; }
    public static Version AppVersion => Assembly.GetExecutingAssembly().GetName().Version!;
    public static event Func<Task>? OnStarted;
    public static event Func<Task>? OnStopping;

    public static async Task SetupAsync()
    {
        var selectedRegistry = ActionHandlerRegistryRuntime.Selected;
        IServiceCollection collection = new ServiceCollection();
        collection.AddLogging(loggingBuilder => loggingBuilder.AddSerilog(dispose: true));

        HttpService = new HttpService();
        await HttpService.SetupAsync(new TouchSocketConfig()
            .SetListenIPHosts(AppConfig.Get().Port)
            .UseAspNetCoreContainer(collection)
            .ConfigureContainer(a => DaemonServiceComposition.ConfigureContainer(a, collection, HttpService, selectedRegistry))
            .ConfigurePlugins(a => DaemonServiceComposition.ConfigurePlugins(a))
        );

        DaemonServiceComposition.AttachDaemonLifecycle(HttpService);
    }


    /// <summary>
    ///     读取配置,添加/api/v1和/login路由的handler,并启动HttpServer。
    ///     路由/api/v1: ws长连接，实现rpc。
    ///     路由/login: http请求，实现登录，返回一个jwt。
    /// </summary>
    public static async Task ServeAsync()
    {
        var gs = HttpService.Resolver.GetRequiredService<GracefulShutdown>();

        OnStopping += () => StopAsync();
        gs.OnShutdown += () => OnStopping.Invoke();

        await HttpService.StartAsync();
        var config = AppConfig.Get();
        Log.Information("[Remote] Ws Server started at ws://0.0.0.0:{0}/api/v1", config.Port);
        Log.Information("[Remote] Http Server started at http://0.0.0.0:{0}/", config.Port);

        await (OnStarted?.Invoke() ?? Task.CompletedTask);

        await gs.WaitForShutdownAsync();

        // 最后释放HttpService
        Log.Debug("[Application] shutting down Http service ...");
        await HttpService.StopAsync();
    }

    private static async Task StopAsync(int timeout = -1)
    {
        var cts = new CancellationTokenSource(timeout);

        var manager = HttpService.Resolver.GetRequiredService<IInstanceManager>();

        Log.Debug("[InstanceManager] stopping instances ...");
        await manager.StopAllInstances(cts.Token);

        Log.Debug("[WsContextContainer] closing websocket connections ...");
        await Task.WhenAll(HttpService.Resolver.GetRequiredService<WsContextContainer>()
            .Select(kv => kv.Value.GetWebsocket().CloseAsync("Daemon exit", cts.Token)));
    }


    public static async Task<bool> InitAsync()
    {
        return await DaemonStartupInitialization.InitializeAsync();
    }
}
