using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCServerLauncher.Daemon.API.Protocol;
using MCServerLauncher.Daemon.Remote.Rpc.Catalog;
using Serilog;
using TouchSocket.Core;
using TouchSocket.Http;
using HttpMethod = TouchSocket.Http.HttpMethod;

namespace MCServerLauncher.Daemon.Remote;

internal sealed class HttpPlugin : PluginBase, IHttpPlugin
{
    private readonly IFrozenProtocolCatalogAccessor _catalogAccessor;

    public HttpPlugin(IFrozenProtocolCatalogAccessor catalogAccessor)
    {
        _catalogAccessor = catalogAccessor ?? throw new ArgumentNullException(nameof(catalogAccessor));
    }

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

    private async Task HandleRequest(IHttpSessionClient client, HttpMethod method, HttpContextEventArgs e)
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
                                new RootResponse("MCServerLauncher Future Daemon CSharp", version, "ok", "v2"),
                                HttpPluginJsonSerializerContext.Default.RootResponse))
                            .AnswerAsync();
                        break;

                    case "/info":
                        await response
                            .SetStatus(200, "Success")
                            .AddHeader("Content-type", "application/json")
                            .AddHeader("Access-Control-Allow-Origin", "*")
                            .SetContent(JsonSerializer.Serialize(
                                new InfoResponse("MCServerLauncher Future Daemon CSharp", version, "v2"),
                                HttpPluginJsonSerializerContext.Default.InfoResponse))
                            .AnswerAsync();
                        break;

                    case "/apifox.json":
                        await response
                            .SetStatus(200, "Success")
                            .AddHeader("Content-type", "application/json; charset=utf-8")
                            .AddHeader("Access-Control-Allow-Origin", "*")
                            .SetContent(await BuildRuntimeApifoxAsync())
                            .AnswerAsync();
                        break;

                    default:
                        if (EmbeddedDocumentation.TryGetResource(request.URL, out var document))
                        {
                            await response
                                .SetStatus(200, "Success")
                                .AddHeader("Content-type", document.ContentType)
                                .AddHeader("Access-Control-Allow-Origin", "*")
                                .SetContent(await EmbeddedDocumentation.ReadContentAsync(document))
                                .AnswerAsync();
                        }

                        break;
                }
            // Others
        }
        catch (Exception ex)
        {
            await response.SetStatus(500, ex.Message).AnswerAsync();
        }
    }
    private Task<byte[]> BuildRuntimeApifoxAsync()
    {
        if (_catalogAccessor.TryGet(out var catalog) && catalog is not null)
        {
            return Task.FromResult(ApifoxProjectGenerator.Generate(catalog.Document, catalog.RpcDefinitions));
        }

        // Catalog is published after plugin admission; fall back to built-in baseline.
        return Task.FromResult(ApifoxProjectGenerator.GenerateBuiltIn());
    }

}

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(HttpPlugin.RootResponse))]
[JsonSerializable(typeof(HttpPlugin.InfoResponse))]
internal partial class HttpPluginJsonSerializerContext : JsonSerializerContext
{
}
