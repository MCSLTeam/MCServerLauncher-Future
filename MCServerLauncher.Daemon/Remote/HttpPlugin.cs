using MCServerLauncher.Daemon.Remote.Authentication;
using Serilog;
using TouchSocket.Core;
using TouchSocket.Http;

namespace MCServerLauncher.Daemon.Remote;

public class HttpPlugin : PluginBase, IHttpPlugin
{
    private readonly IUserService _userService;

    public HttpPlugin(IUserService userService)
    {
        _userService = userService;
    }

    public async Task OnHttpRequest(IHttpSessionClient client, HttpContextEventArgs e)
    {
        var request = e.Context.Request;
        var response = e.Context.Response;


        if (request.IsPost()) await HandlePostRequest(client, e);
        if (request.IsMethod("head")) await response.AddHeader("x-application", "mcsl_daemon_csharp").AnswerAsync();

        await e.InvokeNext();
    }

    private Task HandlePostRequest(IHttpSessionClient client, HttpContextEventArgs e)
    {
        var request = e.Context.Request;
        var response = e.Context.Response;

        try
        {
            if (request.UrlEquals("/login"))
            {
                var usr = request.Query.Get("usr") ?? "";
                var pwd = request.Query.Get("pwd") ?? "";
                var expired = int.Parse(request.Query.Get("expired") ?? "30");

                if (!_userService.Authenticate(usr, pwd, out _))
                    return response.SetStatus(401, "Unauthorized").AnswerAsync();

                var token = JwtUtils.GenerateToken(usr, pwd, expired);
                Log.Information("[Authenticator] Login Success: {0}, token will expire in {1}s",usr,expired);
                return response
                    .SetStatus(200, "success")
                    .SetContent(token)
                    .AnswerAsync();
            }

            return response.SetStatus(404, "Not Found").AnswerAsync();
        }
        catch (Exception ex)
        {
            return response.SetStatus(500, ex.Message).AnswerAsync();
        }
    }
}