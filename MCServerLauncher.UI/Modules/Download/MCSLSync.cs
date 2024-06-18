using System.Collections.Generic;
using System.Threading.Tasks;
using System.Net.Http;
using MCServerLauncher.UI.Helpers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MCServerLauncher.UI.Modules.Download
{
    internal class MCSLSync
    {
        private readonly string EndPoint = "https://sync.mcsl.com.cn/api";

        public class MCSLSyncCoreDetail
        {
            public string Core { get; set; }
            public string MinecraftVersion { get; set; }
            public string CoreVersion { get; set; }
            public string DownloadUrl { get; set; }
            //public string SHA1 { get; set; }
        }
        public async Task<List<string>> GetCoreInfo()
        {
            NetworkUtils NetworkUtils = new();
            HttpResponseMessage Response = await NetworkUtils.SendGetRequest($"{EndPoint}/core");
            if (Response.IsSuccessStatusCode)
            {
                return JsonConvert.DeserializeObject<JToken>(await Response.Content.ReadAsStringAsync()).SelectToken("data").ToObject<List<string>>();
            } else {
                return null;
            }
        }
        public async Task<List<string>> GetMinecraftVersions(string Core)
        {
            NetworkUtils NetworkUtils = new();
            HttpResponseMessage Response = await NetworkUtils.SendGetRequest($"{EndPoint}/core/{Core}");
            if (Response.IsSuccessStatusCode)
            {
                return JsonConvert.DeserializeObject<JToken>(await Response.Content.ReadAsStringAsync()).SelectToken("data").SelectToken("versions").ToObject<List<string>>();
            }
            else
            {
                return null;
            }
        }
        public async Task<List<string>> GetCoreVersions(string Core, string MinecraftVersion)
        {
            NetworkUtils NetworkUtils = new();
            HttpResponseMessage Response = await NetworkUtils.SendGetRequest($"{EndPoint}/core/{Core}/{MinecraftVersion}");
            if (Response.IsSuccessStatusCode)
            {
                return JsonConvert.DeserializeObject<JToken>(await Response.Content.ReadAsStringAsync()).SelectToken("data").SelectToken("builds").ToObject<List<string>>();
            }
            else
            {
                return null;
            }
        }
        public async Task<MCSLSyncCoreDetail> GetCoreDetail(string Core, string MinecraftVersion, string CoreVersion)
        {
            NetworkUtils NetworkUtils = new();
            HttpResponseMessage Response = await NetworkUtils.SendGetRequest($"{EndPoint}/core/{Core}/{MinecraftVersion}/{CoreVersion}");
            if (Response.IsSuccessStatusCode)
            {
                JToken RemoteMCSLSyncCoreDetail = JsonConvert.DeserializeObject<JToken>(await Response.Content.ReadAsStringAsync()).SelectToken("data").SelectToken("build");
                return new MCSLSyncCoreDetail
                {
                    Core = RemoteMCSLSyncCoreDetail.SelectToken("core_type").ToString(),
                    MinecraftVersion = RemoteMCSLSyncCoreDetail.SelectToken("mc_version").ToString(),
                    CoreVersion = RemoteMCSLSyncCoreDetail.SelectToken("core_version").ToString(),
                    DownloadUrl = RemoteMCSLSyncCoreDetail.SelectToken("download_url").ToString()
                    //SHA1 = RemoteMCSLSyncCoreDetail.SelectToken("sha1").ToString()
                };
            }
            else
            {
                return null;
            }
        }
    }
}
