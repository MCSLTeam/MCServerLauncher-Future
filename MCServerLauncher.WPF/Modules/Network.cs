using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MCServerLauncher.Common;
using Serilog;
using static MCServerLauncher.WPF.App;

namespace MCServerLauncher.WPF.Modules
{
    public static class Network
    {
        private static readonly HttpClient Client = new();
        public static string CommonUserAgent = $"MCServerLauncher/{AppVersion}";

        public static string BrowserUserAgent =
            $"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/58.0.3029.110 Safari/537.3 MCServerLauncher/{AppVersion}";

        public static async Task<HttpResponseMessage> SendGetRequest(string url, bool useBrowserUserAgent = false,CancellationToken cancellationToken=default)
        {
            Log.Information($"[Net] Try to get url \"{url}\"");
            Client.DefaultRequestHeaders.Add("User-Agent", useBrowserUserAgent ? BrowserUserAgent : CommonUserAgent);
            return await Client.GetAsync(url,cancellationToken);
        }

        public static async Task<HttpResponseMessage> SendPostRequest(string url, string data,
            bool useBrowserUserAgent = false,CancellationToken cancellationToken = default)
        {
            Log.Information($"[Net] Try to post url \"{url}\" with data {data}");
            Client.DefaultRequestHeaders.Add("User-Agent", useBrowserUserAgent ? BrowserUserAgent : CommonUserAgent);
            return await Client.PostAsync(url, new StringContent(data, Encoding.UTF8, "application/json"),cancellationToken);
        }

        public static void OpenUrl(string url)
        {
            try
            {
                Process.Start(url);
                Log.Information($"[Net] Try to open url \"{url}\"");
            }
            catch (Exception ex)
            {
                Log.Error($"[Net] Failed to open url \"{url}\". Reason: {ex.Message}");
            }
        }
    }
}
