using System.Reflection;
using MCServerLauncher.Daemon.Remote.Authentication;
using MCServerLauncher.Daemon.Remote.Authentication.PermissionSystem;
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

    private static async Task HandleRequest(IHttpSessionClient client, HttpMethod method, HttpContextEventArgs e)
    {
        var request = e.Context.Request;
        var response = e.Context.Response;
        Log.Information($"Method: {method}, Path: {request.URL}");
        try
        {
            if (method == HttpMethod.Get)
                switch (request.URL.ToLower())
                {
                    case "/":
                        await response
                            .SetStatus(200, "Success")
                            .AddHeader("Content-type", "application/json")
                            .AddHeader("Access-Control-Allow-Origin", "*")
                            .SetContent(new JObject
                            {
                                ["message"] = "MCServerLauncher Future Daemon CSharp",
                                ["version"] =
                                    Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown",
                                ["status"] = "ok",
                                ["api_version"] = "v1"
                            }.ToString())
                            .AnswerAsync();
                        break;

                    case "/info":
                        await response
                            .SetStatus(200, "Success")
                            .AddHeader("Content-type", "application/json")
                            .AddHeader("Access-Control-Allow-Origin", "*")
                            .SetContent(new JObject
                            {
                                ["name"] = "MCServerLauncher Future Daemon CSharp",
                                ["version"] =
                                    Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown",
                                ["api_version"] = "v1"
                            }.ToString())
                            .AnswerAsync();
                        break;
                }
            else if (method == HttpMethod.Post)
                switch (request.URL.ToLower())
                {
                    case "/subtoken":
                        var token = "";
                        var permissions = "";
                        var expires = 30;

                        var form = await request.GetFormCollectionAsync();
                        if (form.ContainsKey("token") && form.ContainsKey("permissions"))
                        {
                            token = form["token"];
                            permissions = form["permissions"];
                        }

                        if (form.TryGetValue("expires", out var expiresStr))
                        {
                            if (int.TryParse(expiresStr, out var expiresInt))
                            {
                                expires = expiresInt;
                            }
                            else
                            {
                                await response.SetStatus(400, "Invalid expires").SetContent("").AnswerAsync();
                                return;
                            }
                        }

                        if (Permissions.IsValid(permissions))
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
                        Log.Information("[Authenticator] Subtoken {0} generated, expiring in {1} seconds", jwt,
                            expires);
                        await response
                            .SetStatus(200, "Success")
                            .AddHeader("Content-type", "text/plain")
                            .SetContent(jwt)
                            .AnswerAsync();
                        break;
                }
            // Others
        }
        catch (Exception ex)
        {
            await response.SetStatus(500, ex.Message).AnswerAsync();
        }
    }
}