using MCServerLauncher.Common.Network;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace MCServerLauncher.Common.DownloadProvider
{
    public static class FastMirror
    {
        private const string _endPoint = "https://download.fastmirror.net/api/v3";

        /// <summary>
        ///    Get FastMirror core info.
        /// </summary>
        /// <returns>List of core name.</returns>
        public async static Task<List<FastMirrorCoreInfo>?> GetCoreInfo()
        {
            var response = await HttpHelper.SendGetRequest(_endPoint);
            if (!response.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var data = doc.RootElement.GetProperty("data");
            var results = new List<FastMirrorCoreInfo>();
            foreach (var item in data.EnumerateArray())
            {
                results.Add(new FastMirrorCoreInfo
                {
                    Name = item.GetProperty("name").GetString(),
                    Tag = item.GetProperty("tag").GetString(),
                    HomePage = item.GetProperty("homepage").GetString(),
                    Recommend = item.GetProperty("recommend").GetBoolean(),
                    MinecraftVersions = JsonSerializer.Deserialize<List<string>>(item.GetProperty("mc_versions").GetRawText())
                });
            }
            return results;
        }

        /// <summary>
        ///    Get FastMirror core detail.
        /// </summary>
        /// <param name="core"></param>
        /// <param name="minecraftVersion"></param>
        /// <returns></returns>
        public static async Task<List<FastMirrorCoreDetail>?> GetCoreDetail(string? core, string minecraftVersion)
        {
            var response =
                await HttpHelper.SendGetRequest($"{_endPoint}/{core}/{minecraftVersion}?offset=0&limit=25");
            if (!response.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var builds = doc.RootElement.GetProperty("data").GetProperty("builds");
            var results = new List<FastMirrorCoreDetail>();
            foreach (var item in builds.EnumerateArray())
            {
                results.Add(new FastMirrorCoreDetail
                {
                    Name = item.GetProperty("name").GetString(),
                    MinecraftVersion = item.GetProperty("mc_version").GetString(),
                    CoreVersion = item.GetProperty("core_version").GetString(),
                    Sha1 = item.GetProperty("sha1").GetString()
                });
            }
            return results;
        }

        /// <summary>
        ///    Get download url.
        /// </summary>
        /// <param name="core"></param>
        /// <param name="minecraftVersion"></param>
        /// <param name="coreVersion"></param>
        /// <returns></returns>
        public static string CombineDownloadUrl(string core, string minecraftVersion, string coreVersion) => $"https://download.fastmirror.net/download/{core}/{minecraftVersion}/{coreVersion}";
#nullable enable
        public class FastMirrorCoreInfo
        {
            public string? Name { get; set; }
            public string? Tag { get; set; }
            public string? HomePage { get; set; }
            public bool Recommend { get; set; }
            public List<string>? MinecraftVersions { get; set; }
        }

        public class FastMirrorCoreDetail
        {
            public string? Name { get; set; }
            public string? MinecraftVersion { get; set; }
            public string? CoreVersion { get; set; }
            public string? Sha1 { get; set; }
        }
#nullable disable
    }
}