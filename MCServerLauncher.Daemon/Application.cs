using MCServerLauncher.Common.ProtoType;
using MCServerLauncher.Daemon.Minecraft.Server;
using MCServerLauncher.Daemon.Remote;
using MCServerLauncher.Daemon.Remote.Action;
using MCServerLauncher.Daemon.Remote.Authentication;
using MCServerLauncher.Daemon.Remote.Event;
using MCServerLauncher.Daemon.Storage;
using MCServerLauncher.Daemon.Utils.Cache;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using TouchSocket.Core;
using TouchSocket.Http;
using TouchSocket.Sockets;

namespace MCServerLauncher.Daemon;

public class Application
{
    private readonly HttpService _httpService;
    public static readonly DateTime StartTime = DateTime.Now;

    public Application()
    {
        IServiceCollection collection = new ServiceCollection();
        collection.AddScoped<WsServiceContext>();


        _httpService = new HttpService();
        _httpService.Setup(new TouchSocketConfig()
            .SetListenIPHosts(AppConfig.Get().Port)
            .UseAspNetCoreContainer(collection)
            .ConfigureContainer(a =>
            {
                a.RegisterSingleton<IServiceCollection>(collection)
                    .RegisterSingleton<IHttpService>(_httpService)
                    .RegisterSingleton<IActionService, ActionProcessor>()
                    .RegisterSingleton<IEventService, EventService>()
                    .RegisterSingleton<ActionHandlerRegistry>()
                    .RegisterSingleton<IInstanceManager>(InstanceManager.Create())
                    .RegisterSingleton<IAsyncTimedCacheable<List<JavaInfo>>>(
                        new AsyncTimedCache<List<JavaInfo>>(
                            () => JavaScanner.ScanJava(),
                            TimeSpan.FromMilliseconds(60000)
                        )
                    );
            })
            .ConfigurePlugins(
                a =>
                {
                    a.Add<HttpPlugin>();
                    a.UseWebSocket()
                        .SetWSUrl("/api/v1")
                        .SetVerifyConnection(async (_, context) =>
                        {
                            // if (!context.Request.IsUpgrade()) return false;

                            try
                            {
                                var token = context.Request.Query["token"];

                                if (token != null && (AppConfig.Get().MainToken.Equals(token) ||
                                                      JwtUtils.ValidateToken(token)))
                                    return true;

                                await context.Response.SetStatus(401, "Unauthorized").AnswerAsync();
                                return false;
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine(e);
                                await context.Response.SetStatus(500, e.Message).AnswerAsync();
                                return false;
                            }
                        })
                        .UseAutoPong();

                    a.Add<WebsocketPlugin>();
                    a.UseDefaultHttpServicePlugin();
                })
        );

        PostApplicationContainerBuilt();
    }

    private void PostApplicationContainerBuilt()
    {
        var resolver = _httpService.Resolver;
        resolver.GetRequiredService<ActionHandlerRegistry>().RegisterHandlers();
    }

    /// <summary>
    ///     读取配置,添加/api/v1和/login路由的handler,并启动HttpServer。
    ///     路由/api/v1: ws长连接，实现rpc。
    ///     路由/login: http请求，实现登录，返回一个jwt。
    /// </summary>
    public async Task StartAsync()
    {
        var config = AppConfig.Get();
        await _httpService.StartAsync();
        Log.Information("[Remote] Ws Server started at ws://0.0.0.0:{0}/api/v1", config.Port);
        Log.Information("[Remote] Http Server started at http://0.0.0.0:{0}/", config.Port);

        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };
        while (!cts.IsCancellationRequested)
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), cts.Token);
            }
            catch (TaskCanceledException)
            {
                break;
            }

        Log.Information("[Remote] Stopping...");
        await StopAsync();
    }

    public async Task StopAsync(int timeout = 5000)
    {
        var cts = new CancellationTokenSource();

        var manager = _httpService.Resolver.GetRequiredService<IInstanceManager>();

        cts.CancelAfter(timeout);
        // TODO 修复不能软停止实例的问题
        await manager.StopAllInstances(cts.Token);

        await _httpService.StopAsync();
    }
}