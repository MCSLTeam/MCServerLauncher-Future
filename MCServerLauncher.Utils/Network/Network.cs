using System.Net.Http;

namespace MCServerLauncher.Utils;

/// <summary>
///     下载 provider 使用的轻量 HTTP 辅助。User-Agent 由宿主（WPF/Daemon）设置。
/// </summary>
public static class Network
{
    private static readonly HttpClient Client = new();

    /// <summary>
    ///     宿主可在启动时覆盖，例如 $"MCServerLauncher/{version}"。
    /// </summary>
    public static string UserAgent { get; set; } = "MCServerLauncher";

    public static string BrowserUserAgent { get; set; } =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0";

    public static async Task<HttpResponseMessage> SendGetRequest(string url, bool useBrowserUserAgent = false,
        CancellationToken cancellationToken = default)
  {
        Client.DefaultRequestHeaders.Remove("User-Agent");
        Client.DefaultRequestHeaders.Add("User-Agent", useBrowserUserAgent ? BrowserUserAgent : UserAgent);
        return await Client.GetAsync(url, cancellationToken);
    }
}
