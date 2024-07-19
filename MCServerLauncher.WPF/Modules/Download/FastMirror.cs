using System.Collections.Generic;
using System.Threading.Tasks;
using System.Net.Http;
using MCServerLauncher.WPF.Helpers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MCServerLauncher.WPF.Modules.Download
{
    internal class FastMirror
    {
        private readonly string EndPoint = "https://download.fastmirror.net/api/v3";

        public class FastMirrorCoreInfo
        {
            public string Name { get; set; }
            public string Tag { get; set; }
            public string HomePage { get; set; }
            public bool Recommend { get; set; }
            public List<string> MinecraftVersions { get; set; }
        }

        public class FastMirrorCoreDetail
        {

            public string Name { get; set; }
            public string MinecraftVersion { get; set; }
            public string CoreVersion { get; set; }
            public string SHA1 { get; set; }
        }
        private string FormatFastMirrorCoreTag(string OriginalTag)
        {
            switch (OriginalTag)
            {
                case "proxy":
                    return "代理";
                case "vanilla":
                    return "原版";
                case "pure":
                    return "纯净";
                case "mod":
                    return "模组";
                case "bedrock":
                    return "基岩";
                default:
                    return OriginalTag;
            }
        }
        public async Task<List<FastMirrorCoreInfo>> GetCoreInfo()
        {
            HttpResponseMessage Response = await NetworkUtils.SendGetRequest(EndPoint);
            if (Response.IsSuccessStatusCode)
            {
                JToken RemoteFastMirrorCoreInfoList = JsonConvert.DeserializeObject<JToken>(await Response.Content.ReadAsStringAsync()).SelectToken("data");
                List<FastMirrorCoreInfo> FastMirrorCoreInfoList = new();
                foreach (JToken FastMirrorCoreInfo in RemoteFastMirrorCoreInfoList)
                {
                    FastMirrorCoreInfoList.Add(new FastMirrorCoreInfo
                    {
                        Name = FastMirrorCoreInfo.SelectToken("name").ToString(),
                        Tag = FormatFastMirrorCoreTag(FastMirrorCoreInfo.SelectToken("tag").ToString()),
                        HomePage = FastMirrorCoreInfo.SelectToken("homepage").ToString(),
                        Recommend = FastMirrorCoreInfo.SelectToken("recommend").ToObject<bool>(),
                        MinecraftVersions = FastMirrorCoreInfo.SelectToken("mc_versions").ToObject<List<string>>()
                    });
                }
                return FastMirrorCoreInfoList;
            } else {
                return null;
            }
        }
        public async Task<List<FastMirrorCoreDetail>> GetCoreDetail(string Core, string MinecraftVersion)
        {
            HttpResponseMessage Response = await NetworkUtils.SendGetRequest($"{EndPoint}/{Core}/{MinecraftVersion}?offset=0&limit=25");
            if (Response.IsSuccessStatusCode)
            {
                JToken RemoteFastMirrorCoreDetailList = JsonConvert.DeserializeObject<JToken>(await Response.Content.ReadAsStringAsync()).SelectToken("data").SelectToken("builds");
                List<FastMirrorCoreDetail> FastMirrorCoreDetailList = new();
                foreach (JToken RemoteFastMirrorCoreDetail in RemoteFastMirrorCoreDetailList)
                {
                    FastMirrorCoreDetailList.Add(new FastMirrorCoreDetail
                    {
                        Name = RemoteFastMirrorCoreDetail.SelectToken("name").ToString(),
                        MinecraftVersion = RemoteFastMirrorCoreDetail.SelectToken("mc_version").ToString(),
                        CoreVersion = RemoteFastMirrorCoreDetail.SelectToken("core_version").ToString(),
                        SHA1 = RemoteFastMirrorCoreDetail.SelectToken("sha1").ToString()
                    });
                }
                return FastMirrorCoreDetailList;
            } else {
                return null;
            }
        }
        public string CombineDownloadUrl(string Core, string MinecraftVersion, string CoreVersion)
        {
            return $"https://download.fastmirror.net/download/{Core}/{MinecraftVersion}/{CoreVersion}";
        }
    }
}
