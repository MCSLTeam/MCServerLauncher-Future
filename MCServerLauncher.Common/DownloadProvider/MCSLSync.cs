using MCServerLauncher.Common.Network;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace MCServerLauncher.Common.DownloadProvider
{
    public static class MCSLSync
    {
        private const string _endPoint = "https://sync.mcsl.com.cn/api";

        /// <summary>
        ///    Get core info from MCSL-Sync
        /// </summary>
        /// <returns>List of core name.</returns>
        public static async Task<List<string>?> GetCoreInfo()
        {
            var response = await HttpHelper.SendGetRequest($"{_endPoint}/core");
            if (response.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                return JsonSerializer.Deserialize<List<string>>(doc.RootElement.GetProperty("data").GetRawText());
            }
            return null;
        }

        /// <summary>
        ///    Get Minecraft versions of specific core from MCSL-Sync
        /// </summary>
        /// <param name="core"></param>
        /// <returns></returns>
        public static async Task<List<string>?> GetMinecraftVersions(string core)
        {
            var response = await HttpHelper.SendGetRequest($"{_endPoint}/core/{core}");
            if (response.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                return JsonSerializer.Deserialize<List<string>>(doc.RootElement.GetProperty("data").GetProperty("versions").GetRawText());
            }
            return null;
        }

        /// <summary>
        ///    Get core versions of specific Minecraft version from MCSL-Sync
        /// </summary>
        /// <param name="core"></param>
        /// <param name="minecraftVersion"></param>
        /// <returns></returns>
        public static async Task<List<string>?> GetCoreVersions(string core, string minecraftVersion)
        {
            var response = await HttpHelper.SendGetRequest($"{_endPoint}/core/{core}/{minecraftVersion}");
            if (response.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                return JsonSerializer.Deserialize<List<string>>(doc.RootElement.GetProperty("data").GetProperty("builds").GetRawText());
            }
            return null;
        }

        /// <summary>
        ///    Get core detail from MCSL-Sync
        /// </summary>
        /// <param name="core"></param>
        /// <param name="minecraftVersion"></param>
        /// <param name="coreVersion"></param>
        /// <returns></returns>
        public static async Task<MCSLSyncCoreDetail?> GetCoreDetail(string? core, string minecraftVersion, string coreVersion)
        {
            var response =
                await HttpHelper.SendGetRequest($"{_endPoint}/core/{core}/{minecraftVersion}/{coreVersion}");
            if (!response.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var build = doc.RootElement.GetProperty("data").GetProperty("build");
            return new MCSLSyncCoreDetail
            {
                Core = build.GetProperty("core_type").GetString(),
                MinecraftVersion = build.GetProperty("mc_version").GetString(),
                CoreVersion = build.GetProperty("core_version").GetString(),
                DownloadUrl = build.GetProperty("download_url").GetString()
                //SHA1 = build.GetProperty("sha1").GetString()
            };
        }
#nullable enable
        public class MCSLSyncCoreDetail
        {
            public string? Core { get; set; }
            public string? MinecraftVersion { get; set; }
            public string? CoreVersion { get; set; }

            public string? DownloadUrl { get; set; }
            //public string SHA1 { get; set; }
        }
#nullable disable
    }
}