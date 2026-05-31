using System.Collections.Generic;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MCServerLauncher.Utils;

/// <summary>
///     下载 provider 使用的轻量 HTTP 辅助。User-Agent 默认取本库版本，宿主可覆盖。
/// </summary>
public static class Network
{
    private static readonly HttpClient Client = new();

    /// <summary>
    ///     默认 $"MCServerLauncherUtils/{库版本}"，宿主可在启动时覆盖。
    /// </summary>
    public static string UserAgent { get; set; } =
        $"MCServerLauncherUtils/{Assembly.GetExecutingAssembly().GetName().Version}";

    public static string BrowserUserAgent { get; set; } =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0";

    public static Task<HttpResponseMessage> SendGetRequest(string url, bool useBrowserUserAgent = false,
        IDictionary<string, string>? headers = null, int? timeoutSeconds = null,
        CancellationToken cancellationToken = default)
    {
        return Send(HttpMethod.Get, url, null, useBrowserUserAgent, headers, timeoutSeconds, cancellationToken);
}

    public static Task<HttpResponseMessage> SendPostRequest(string url, string data, bool useBrowserUserAgent = false,
        IDictionary<string, string>? headers = null, int? timeoutSeconds = null,
        CancellationToken cancellationToken = default)
    {
        return Send(HttpMethod.Post, url, data, useBrowserUserAgent, headers, timeoutSeconds, cancellationToken);
    }

    public static Task<HttpResponseMessage> SendPutRequest(string url, string data, bool useBrowserUserAgent = false,
        IDictionary<string, string>? headers = null, int? timeoutSeconds = null,
        CancellationToken cancellationToken = default)
    {
     return Send(HttpMethod.Put, url, data, useBrowserUserAgent, headers, timeoutSeconds, cancellationToken);
    }

    public static Task<HttpResponseMessage> SendDeleteRequest(string url, bool useBrowserUserAgent = false,
    IDictionary<string, string>? headers = null, int? timeoutSeconds = null,
 CancellationToken cancellationToken = default)
    {
        return Send(HttpMethod.Delete, url, null, useBrowserUserAgent, headers, timeoutSeconds, cancellationToken);
    }

    private static async Task<HttpResponseMessage> Send(HttpMethod method, string url, string? data,
        bool useBrowserUserAgent, IDictionary<string, string>? headers, int? timeoutSeconds,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.TryAddWithoutValidation("User-Agent", useBrowserUserAgent ? BrowserUserAgent : UserAgent);
        if (headers != null)
    foreach (var header in headers)
             request.Headers.TryAddWithoutValidation(header.Key, header.Value);
      if (data != null)
    request.Content = new StringContent(data, Encoding.UTF8, "application/json");

      if (timeoutSeconds == null)
         return await Client.SendAsync(request, cancellationToken);

using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(System.TimeSpan.FromSeconds(timeoutSeconds.Value));
     return await Client.SendAsync(request, timeoutCts.Token);
    }
}
