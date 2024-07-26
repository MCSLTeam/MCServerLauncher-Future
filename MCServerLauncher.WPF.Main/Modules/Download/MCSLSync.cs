using System.Collections.Generic;
using System.Threading.Tasks;
using MCServerLauncher.WPF.Main.Helpers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MCServerLauncher.WPF.Main.Modules.Download
{
    internal class MCSLSync
    {
        private readonly string _endPoint = "https://sync.mcsl.com.cn/api";

        public async Task<List<string>> GetCoreInfo()
        {
            var response = await NetworkUtils.SendGetRequest($"{_endPoint}/core");
            if (response.IsSuccessStatusCode)
                return JsonConvert.DeserializeObject<JToken>(await response.Content.ReadAsStringAsync())
                    .SelectToken("data")!.ToObject<List<string>>();
            return null;
        }

        public async Task<List<string>> GetMinecraftVersions(string core)
        {
            var response = await NetworkUtils.SendGetRequest($"{_endPoint}/core/{core}");
            if (response.IsSuccessStatusCode)
                return JsonConvert.DeserializeObject<JToken>(await response.Content.ReadAsStringAsync())
                    .SelectToken("data")!.SelectToken("versions")!.ToObject<List<string>>();
            return null;
        }

        public async Task<List<string>> GetCoreVersions(string core, string minecraftVersion)
        {
            var response = await NetworkUtils.SendGetRequest($"{_endPoint}/core/{core}/{minecraftVersion}");
            if (response.IsSuccessStatusCode)
                return JsonConvert.DeserializeObject<JToken>(await response.Content.ReadAsStringAsync())
                    .SelectToken("data")!.SelectToken("builds")!.ToObject<List<string>>();
            return null;
        }

        public async Task<MCSLSyncCoreDetail> GetCoreDetail(string core, string minecraftVersion, string coreVersion)
        {
            var response =
                await NetworkUtils.SendGetRequest($"{_endPoint}/core/{core}/{minecraftVersion}/{coreVersion}");
            if (!response.IsSuccessStatusCode) return null;
            var remoteMCSLSyncCoreDetail = JsonConvert
                .DeserializeObject<JToken>(await response.Content.ReadAsStringAsync()).SelectToken("data")!
                .SelectToken("build");
            return new MCSLSyncCoreDetail
            {
                Core = remoteMCSLSyncCoreDetail!.SelectToken("core_type")!.ToString(),
                MinecraftVersion = remoteMCSLSyncCoreDetail.SelectToken("mc_version")!.ToString(),
                CoreVersion = remoteMCSLSyncCoreDetail.SelectToken("core_version")!.ToString(),
                DownloadUrl = remoteMCSLSyncCoreDetail.SelectToken("download_url")!.ToString()
                //SHA1 = RemoteMCSLSyncCoreDetail.SelectToken("sha1").ToString()
            };
        }

        public class MCSLSyncCoreDetail
        {
            public string Core { get; set; }
            public string MinecraftVersion { get; set; }
            public string CoreVersion { get; set; }

            public string DownloadUrl { get; set; }
            //public string SHA1 { get; set; }
        }
    }
}