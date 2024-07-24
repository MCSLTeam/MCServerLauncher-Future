using System.Text;
using MCServerLauncher.Daemon.Remote.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using WebSocketSharp.Server;
using WebSocketSharp;


namespace MCServerLauncher.Daemon.Remote;

public class Server : IServer
{
    private readonly IServiceProvider _container;

    public Server(IServiceProvider Container)
    {
        _container = Container;
    }

    private void HandleHttpRequest(object _, HttpRequestEventArgs e)
    {
        var request = e.Request;
        e.Response.KeepAlive = false;
        try
        {
            if (request.Url.AbsolutePath == "/login") // /login?usr=xxx&pwd=yyy&expired=0
            {
                var usr = request.QueryString["usr"];
                var pwd = request.QueryString["pwd"];
                var expired = request.QueryString.Contains("expired") ? int.Parse(request.QueryString["expired"]!) : 30;

                var userService = _container.GetRequiredService<IUserService>();

                if (userService.Authenticate(usr, pwd, out var userMeta))
                {
                    var token = JwtUtils.GenerateToken(usr, pwd, expired);
                    var buffer = Encoding.UTF8.GetBytes(token);

                    e.Response.ContentType = "text/plain";
                    e.Response.ContentLength64 = buffer.Length;
                    e.Response.StatusCode = 200;
                    e.Response.OutputStream.Write(buffer, 0, buffer.Length);
                    Log.Information($" [Authenticator] Login Success: {usr}, token will expire in {expired}s");
                }
                else
                {
                    e.Response.StatusCode = 401;
                    var buffer = Encoding.UTF8.GetBytes("Unauthorized");
                    e.Response.ContentType = "text/plain";
                    e.Response.ContentLength64 = buffer.Length;
                    e.Response.OutputStream.Write(buffer, 0, buffer.Length);
                }
            }
            else if (request.Url.AbsolutePath != "/api/v1")
            {
                e.Response.StatusCode = 404;
                var buffer = Encoding.UTF8.GetBytes("Not Found");
                e.Response.ContentType = "text/plain";
                e.Response.ContentLength64 = buffer.Length;
                e.Response.OutputStream.Write(buffer, 0, buffer.Length);
            }
        }
        catch (Exception ex)
        {
            e.Response.StatusCode = 500;
            var buffer = Encoding.UTF8.GetBytes($"Internal Server Error: {ex.Message}");
            e.Response.ContentType = "text/plain";
            e.Response.ContentLength64 = buffer.Length;
            e.Response.OutputStream.Write(buffer, 0, buffer.Length);
        }
        finally
        {
            e.Response.OutputStream.Close();
        }
    }
    

    public void Start()
    {
        // logger
        var logger = _container.GetRequiredService<RemoteLogHelper>();

        // ws
        var server = new HttpServer(Config.Get().Port);
        // server.AddWebSocketService<ServerBehavior>("/api/v1");
        server.AddWebSocketService("/api/v1", () =>
        {
            var scoped = _container.CreateScope();
            var behavior = scoped.ServiceProvider.GetRequiredService<ServerBehavior>();
            return behavior;
        });

        // http
        server.OnGet += HandleHttpRequest;
        
        // load users
        _container.GetRequiredService<IUserService>();

        // start
        server.Start();
        logger.Info($"Ws Server started at ws://{server.Address}:{server.Port}/api/v1");
        logger.Info($"Http Server started at http://{server.Address}:{server.Port}/");
        Console.ReadKey();
        server.Stop();
    }
}