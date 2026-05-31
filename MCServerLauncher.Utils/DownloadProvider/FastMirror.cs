using MCServerLauncher.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MCServerLauncher.Utils.DownloadProvider
{
    public class FastMirror
    {
        private readonly string _endPoint = "https://download.fastmirror.net/api/v3";

        /// <summary>
        ///    Get FastMirror core info.
        /// </summary>
        /// <returns>List of core name.</returns>
        public async Task<List<FastMirrorCoreInfo>?> GetCoreInfo()
        {
            var response = await Network.SendGetRequest(_endPoint);
            if (!response.IsSuccessStatusCode) return null;
            var remoteFastMirrorCoreInfoList = JsonConvert
                .DeserializeObject<JToken>(await response.Content.ReadAsStringAsync())
                ?.SelectToken("data");
            return [.. remoteFastMirrorCoreInfoList!.Select(fastMirrorCoreInfo => new FastMirrorCoreInfo
            {
                Name = fastMirrorCoreInfo.SelectToken("name")!.ToString(),
                Tag = fastMirrorCoreInfo.SelectToken("tag")!.ToString(),
                HomePage = fastMirrorCoreInfo.SelectToken("homepage")!.ToString(),
                Recommend = fastMirrorCoreInfo.SelectToken("recommend")!.ToObject<bool>(),
                MinecraftVersions = fastMirrorCoreInfo.SelectToken("mc_versions")!.ToObject<List<string>>()
            })];
        }

        /// <summary>
        ///    Get FastMirror core detail.
        /// </summary>
        /// <param name="core"></param>
        /// <param name="minecraftVersion"></param>
        /// <returns></returns>
        public async Task<List<FastMirrorCoreDetail>?> GetCoreDetail(string? core, string minecraftVersion)
        {
            var response =
                await Network.SendGetRequest($"{_endPoint}/{core}/{minecraftVersion}?offset=0&limit=25");
            if (!response.IsSuccessStatusCode) return null;
            var remoteFastMirrorCoreDetailList = JsonConvert
                .DeserializeObject<JToken>(await response.Content.ReadAsStringAsync())
                ?.SelectToken("data")!
                .SelectToken("builds");
            return [.. remoteFastMirrorCoreDetailList!.Select(remoteFastMirrorCoreDetail => new FastMirrorCoreDetail
            {
                Name = remoteFastMirrorCoreDetail.SelectToken("name")!.ToString(),
                MinecraftVersion = remoteFastMirrorCoreDetail.SelectToken("mc_version")!.ToString(),
                CoreVersion = remoteFastMirrorCoreDetail.SelectToken("core_version")!.ToString(),
                Sha1 = remoteFastMirrorCoreDetail.SelectToken("sha1")!.ToString()
            })];
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