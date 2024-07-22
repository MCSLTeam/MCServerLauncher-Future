using System.Text;
using MCServerLauncher.Daemon.Remote.Authentication;
using WebSocketSharp.Server;
using WebSocketSharp;

namespace MCServerLauncher.Daemon.Remote;

public class Server
{
    private static void HandleHttpRequest(object sender, HttpRequestEventArgs e)
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

                if (Users.Authenticate(usr, pwd, out var userMeta))
                {
                    var token = JwtUtils.GenerateToken(usr, pwd, expired);
                    var buffer = Encoding.UTF8.GetBytes(token);

                    e.Response.ContentType = "text/plain";
                    e.Response.ContentLength64 = buffer.Length;
                    e.Response.StatusCode = 200;
                    e.Response.OutputStream.Write(buffer, 0, buffer.Length);
                    LogHelper.Info($"Login Success: {usr}, token will expire in {expired}s");
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

    public static void Start()
    {
        // ws
        var server = new HttpServer(Config.Get().Port);
        server.AddWebSocketService<ServerBehavior>("/api/v1");

        // http
        server.OnGet += HandleHttpRequest;

        // init Users
        Users.Init();

        // start
        server.Start();
        LogHelper.Info($"Ws Server started at ws://{server.Address}:{server.Port}/api/v1");
        LogHelper.Info($"Http Server started at http://{server.Address}:{server.Port}/");
        Console.ReadKey();
        server.Stop();
    }
}