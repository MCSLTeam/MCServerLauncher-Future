using Serilog;
using System;
using System.Diagnostics;

namespace MCServerLauncher.WPF.Modules
{
    public static class BrowserHelper
    {
                public static void OpenUrl(string url)
                {
            try
                        {
                                Process.Start(CreateBrowserStartInfo(url));
                                Log.Information($"[Net] Try to open url \"{url}\"");
            }
                        catch (Exception ex)
            {
                                Log.Error($"[Net] Failed to open url \"{url}\". Reason: {ex.Message}");
            }
        }

                internal static ProcessStartInfo CreateBrowserStartInfo(string url) => new()
                {
                        FileName = url,
                        UseShellExecute = true
                };
        }
}
