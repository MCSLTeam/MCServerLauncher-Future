using MCServerLauncher.Daemon.Minecraft.Server;
using MCServerLauncher.Daemon.Remote;
using MCServerLauncher.Daemon.Remote.Action;
using MCServerLauncher.Daemon.Remote.Authentication;
using MCServerLauncher.Daemon.Remote.Event;
using MCServerLauncher.Daemon.Storage;
using MCServerLauncher.Daemon.Utils.Cache;
using Serilog;
using TouchSocket.Core;
using TouchSocket.Http;
using TouchSocket.Sockets;

namespace MCServerLauncher.Daemon;

public class Application
{
    private readonly HttpService _httpService;

    public Application()
    {
        _httpService = new HttpService();
        _httpService.Setup(new TouchSocketConfig()
            .SetListenIPHosts(AppConfig.Get().Port)
            .ConfigureContainer(
                a =>
                {
                    // a.AddConsoleLogger();
                    a.RegisterSingleton<IWebJsonConverter, WebJsonConverter>();
                    a.RegisterSingleton<IUserService, UserService>();
                    a.RegisterSingleton<UserDatabase>();
                    a.RegisterSingleton<ActionHandler>();
                    a.RegisterSingleton<IActionService, ActionServiceImpl>();
                    a.RegisterSingleton<IEventService, EventService>();

                    a.RegisterSingleton<IInstanceManager>(InstanceManager.Create());

                    a.RegisterSingleton<IAsyncTimedCacheable<List<JavaScanner.JavaInfo>>>(
                        new AsyncTimedCache<List<JavaScanner.JavaInfo>>(
                            JavaScanner.ScanJavaAsync,
                            TimeSpan.FromMilliseconds(60000)
                        )
                    );
                }
            ).ConfigurePlugins(
                a =>
                {
                    a.Add<HttpPlugin>();
                    a.UseWebSocket()
                        .SetWSUrl("/api/v1")
                        .SetVerifyConnection(async (_, context) =>
                        {
                            if (!context.Request.IsUpgrade()) return false;

                            try
                            {
                                var userService = a.Resolver.Resolve<IUserService>();

                                var user = await userService.AuthenticateAsync(context.Request.Query["token"] ?? "");
                                if (user != null)
                                {
                                    context.Request.Headers["user"] = user.Name;
                                    return true;
                                }

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

    public async Task StopAsync()
    {
        await _httpService.StopAsync();
    }
}