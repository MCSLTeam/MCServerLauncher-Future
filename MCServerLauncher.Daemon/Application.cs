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
                    a.RegisterSingleton<IActionService, ActionService>();
                    a.RegisterSingleton<IEventService, EventService>();

                    a.RegisterSingleton<IAsyncTimedCacheable<List<JavaScanner.JavaInfo>>>(
                        new AsyncTimedCache<List<JavaScanner.JavaInfo>>(
                            JavaScanner.ScanJavaAsync,
                            TimeSpan.FromMilliseconds(60)
                        )
                    );
                }
            ).ConfigurePlugins(
                a =>
                {
                    a.Add<HttpPlugin>();
                    a.UseWebSocket()
                        .SetWSUrl("/api/v1")
                        .SetVerifyConnection((client, context) =>
                        {
                            if (!context.Request.IsUpgrade()) return false;

                            try
                            {
                                var userService = a.Resolver.Resolve<IUserService>();

                                var (usr, pwd) = JwtUtils.ValidateToken(context.Request.Query["token"] ?? "");
                                if (userService.Authenticate(usr, pwd, out _))
                                {
                                    context.Request.Headers["user"] = usr;
                                    return true;
                                }

                                context.Response.SetStatus(401, "Unauthorized").AnswerAsync();
                                return false;
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine(e);
                                context.Response.SetStatus(500, e.Message).AnswerAsync();
                                return false;
                            }
                        })
                        .UseAutoPong();

                    a.Add<WebsocketPlugin>();
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
        Console.ReadKey();
    }

    public async Task StopAsync()
    {
        await _httpService.StopAsync();
    }
}