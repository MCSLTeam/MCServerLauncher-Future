using System.Reflection;
using System.Text.RegularExpressions;
using MCServerLauncher.Daemon.Remote.Authentication;
using Newtonsoft.Json.Linq;
using Serilog;
using TouchSocket.Core;
using TouchSocket.Http;
using HttpMethod = TouchSocket.Http.HttpMethod;

namespace MCServerLauncher.Daemon.Remote;

public class HttpPlugin : PluginBase, IHttpPlugin
{
    public async Task OnHttpRequest(IHttpSessionClient client, HttpContextEventArgs e)
    {
        await HandleRequest(client, e.Context.Request.Method, e);
        await e.InvokeNext();
    }

    private async Task HandleRequest(IHttpSessionClient client, HttpMethod method, HttpContextEventArgs e)
    {
        var request = e.Context.Request;
        var response = e.Context.Response;

        try
        {
            if (method == HttpMethod.Get && request.UrlEquals("/info"))
            {
                await response
                    .SetStatus(200, "Success")
                    .AddHeader("Content-type", "application/json")
                    .SetContent(new JObject
                    {
                        ["name"] = "MCServerLauncher Future Daemon CSharp",
                        ["version"] = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown",
                        ["apiVersion"] = "v1",
                    }.ToString())
                    .AnswerAsync();
            }
            else if (method == HttpMethod.Post && request.UrlEquals("/subtoken"))
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
                    .SetStatus(200, "Success")
                    .AddHeader("Content-type", "text/plain")
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