using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCServerLauncher.Daemon.Remote.Authentication;
using Serilog;
using TouchSocket.Core;
using TouchSocket.Http;
using HttpMethod = TouchSocket.Http.HttpMethod;

namespace MCServerLauncher.Daemon.Remote;

public class HttpPlugin : PluginBase, IHttpPlugin
{
    internal readonly record struct RootResponse(
        [property: JsonPropertyName("message")]
        string Message,
        [property: JsonPropertyName("version")]
        string Version,
        [property: JsonPropertyName("status")]
        string Status,
        [property: JsonPropertyName("api_version")]
        string ApiVersion);

    internal readonly record struct InfoResponse(
        [property: JsonPropertyName("name")]
        string Name,
        [property: JsonPropertyName("version")]
        string Version,
        [property: JsonPropertyName("api_version")]
        string ApiVersion);

    public async Task OnHttpRequest(IHttpSessionClient client, HttpContextEventArgs e)
    {
        await HandleRequest(client, e.Context.Request.Method, e);
        await e.InvokeNext();
    }

    private static async Task HandleRequest(IHttpSessionClient client, HttpMethod method, HttpContextEventArgs e)
    {
        var request = e.Context.Request;
        var response = e.Context.Response;
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
        Log.Verbose("Method: {Method}, Path: {URL}", method, request.URL);
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
                            .SetContent(JsonSerializer.Serialize(
                                new RootResponse("MCServerLauncher Future Daemon CSharp", version, "ok", "v1"),
                                HttpPluginJsonSerializerContext.Default.RootResponse))
                            .AnswerAsync();
                        break;

                    case "/info":
                        await response
                            .SetStatus(200, "Success")
                            .AddHeader("Content-type", "application/json")
                            .AddHeader("Access-Control-Allow-Origin", "*")
                            .SetContent(JsonSerializer.Serialize(
                                new InfoResponse("MCServerLauncher Future Daemon CSharp", version, "v1"),
                                HttpPluginJsonSerializerContext.Default.InfoResponse))
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

                        if (!Permissions.IsValid(permissions))
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
                        Log.Debug("[Authenticator] Sub-token {0} generated, expiring in {1} seconds", jwt,
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

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(HttpPlugin.RootResponse))]
[JsonSerializable(typeof(HttpPlugin.InfoResponse))]
internal partial class HttpPluginJsonSerializerContext : JsonSerializerContext
{
}
