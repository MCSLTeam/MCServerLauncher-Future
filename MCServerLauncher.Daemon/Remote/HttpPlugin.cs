using System.Text.RegularExpressions;
using MCServerLauncher.Daemon.Remote.Authentication;
using Serilog;
using TouchSocket.Core;
using TouchSocket.Http;

namespace MCServerLauncher.Daemon.Remote;

public class HttpPlugin : PluginBase, IHttpPlugin
{
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
            if (request.UrlEquals("/subtoken"))
            {
                var token = request.Forms["token"] ?? "";
                var permissions = request.Forms["permissions"] ?? "*";
                var expires = 30;

                try
                {
                    expires = int.Parse(request.Forms["expires"] ?? "30");
                }
                catch (Exception)
                {
                    await response.SetStatus(400, "Invalid expire time").SetContent("").AnswerAsync();
                    return;
                }

                if (Permissions.Pattern.IsMatch(permissions))
                {
                    await response.SetStatus(400, "Invalid permissions").SetContent("").AnswerAsync();
                    return;
                }

                if (!token.Equals(AppConfig.Get().MainToken))
                {
                    await response.SetStatus(401, "Unauthorized").SetContent("").AnswerAsync();
                    return;
                }

                var jwt = JwtUtils.GenerateToken(permissions, expires);
                Log.Information("[Authenticator] Subtoken {0} generated, expiring in {1} seconds", jwt, expires);
                await response
                    .SetStatus(200, "success")
                    .SetContent(jwt)
                    .AnswerAsync();
            }
        }
        catch (Exception ex)
        {
            await response.SetStatus(500, ex.Message).AnswerAsync();
        }
    }
}