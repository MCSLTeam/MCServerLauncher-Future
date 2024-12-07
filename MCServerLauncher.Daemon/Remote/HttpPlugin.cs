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

    private async Task HandlePostRequest(IHttpSessionClient client, HttpContextEventArgs e)
    {
        var request = e.Context.Request;
        var response = e.Context.Response;

        try
        {
            if (request.UrlEquals("/login"))
            {
                var usr = request.Forms["usr"] ?? "";
                var pwd = request.Forms["pwd"] ?? "";
                var expired = int.Parse(request.Forms["expired"] ?? "30");

                if (!await _userService.AuthenticateAsync(usr, pwd))
                {
                    await response.SetStatus(401, "Unauthorized").SetContent("").AnswerAsync();
                    return;
                }

                var token = await _userService.GenerateTokenAsync(usr, expired);
                Log.Information("[Authenticator] Login Success: {0}, token will expire in {1}s", usr, expired);
                await response
                    .SetStatus(200, "success")
                    .SetContent(token)
                    .AnswerAsync();
            }
        }
        catch (Exception ex)
        {
            await response.SetStatus(500, ex.Message).AnswerAsync();
        }
    }
}