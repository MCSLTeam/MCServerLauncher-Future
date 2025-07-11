using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MCServerLauncher.WPF.Modules.DownloadProvider
{
    internal class FastMirror
    {
        private readonly string _endPoint = "https://download.fastmirror.net/api/v3";

        /// <summary>
        ///    Prettier tag for FastMirror core.
        /// </summary>
        /// <param name="originalTag"></param>
        /// <returns></returns>
        private static string FormatFastMirrorCoreTag(string originalTag)
        {
            return originalTag switch
            {
                "proxy" => Lang.Tr["DownloadModule_FastMirrorProxyType"],
                "vanilla" => Lang.Tr["DownloadModule_FastMirrorVanillaType"],
                "pure" => Lang.Tr["DownloadModule_FastMirrorPureType"],
                "mod" => Lang.Tr["DownloadModule_FastMirrorModType"],
                "bedrock" => Lang.Tr["DownloadModule_FastMirrorBedrockType"],
                _ => originalTag
            };
        }

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
            return remoteFastMirrorCoreInfoList!.Select(fastMirrorCoreInfo => new FastMirrorCoreInfo
            {
                Name = fastMirrorCoreInfo.SelectToken("name")!.ToString(),
                Tag = FormatFastMirrorCoreTag(fastMirrorCoreInfo.SelectToken("tag")!.ToString()),
                HomePage = fastMirrorCoreInfo.SelectToken("homepage")!.ToString(),
                Recommend = fastMirrorCoreInfo.SelectToken("recommend")!.ToObject<bool>(),
                MinecraftVersions = fastMirrorCoreInfo.SelectToken("mc_versions")!.ToObject<List<string>>()
            })
                .ToList();
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
            return remoteFastMirrorCoreDetailList!.Select(remoteFastMirrorCoreDetail => new FastMirrorCoreDetail
            {
                Name = remoteFastMirrorCoreDetail.SelectToken("name")!.ToString(),
                MinecraftVersion = remoteFastMirrorCoreDetail.SelectToken("mc_version")!.ToString(),
                CoreVersion = remoteFastMirrorCoreDetail.SelectToken("core_version")!.ToString(),
                Sha1 = remoteFastMirrorCoreDetail.SelectToken("sha1")!.ToString()
            }).ToList();
        }

        /// <summary>
        ///    Get download url.
        /// </summary>
        /// <param name="core"></param>
        /// <param name="minecraftVersion"></param>
        /// <param name="coreVersion"></param>
        /// <returns></returns>
        public string CombineDownloadUrl(string core, string minecraftVersion, string coreVersion)
        {
            return $"https://download.fastmirror.net/download/{core}/{minecraftVersion}/{coreVersion}";
        }
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