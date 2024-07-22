using MCServerLauncher.WPF.Main.Helpers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace MCServerLauncher.WPF.Main.Modules.Download
{
    internal class PolarsMirror
    {
        private readonly string EndPoint = "https://mirror.polars.cc/api/query/minecraft";

        public class PolarsMirrorCoreInfo
        {
            public string Name { get; set; }
            public int Id { get; set; }
            public string Description { get; set; }
            public string IconUrl { get; set; }
        }

        public class PolarsMirrorCoreDetail
        {
            public string FileName { get; set; }
            public string DownloadUrl { get; set; }
        }

        // Unavailable service for now
        //public class PolarsServerPackInfo
        //{
        //    public string Name { get; set; }
        //    public int Id { get; set; }
        //    public string Description { get; set; }
        //    public string Url { get; set; }
        //    public string MinecraftVersion { get; set; }
        //}

        public async Task<List<PolarsMirrorCoreInfo>> GetCoreInfo()
        {
            HttpResponseMessage Response = await NetworkUtils.SendGetRequest($"{EndPoint}/core");
            if (Response.IsSuccessStatusCode)
            {
                JToken RemotePolarsCoreInfoList = JsonConvert.DeserializeObject<JToken>(await Response.Content.ReadAsStringAsync());
                List<PolarsMirrorCoreInfo> PolarsCoreInfoList = new();
                foreach (JToken PolarsCoreInfo in RemotePolarsCoreInfoList)
                {
                    PolarsCoreInfoList.Add(new PolarsMirrorCoreInfo
                    {
                        Name = PolarsCoreInfo.SelectToken("name").ToString(),
                        Id = PolarsCoreInfo.SelectToken("id").ToObject<int>(),
                        Description = PolarsCoreInfo.SelectToken("description").ToString(),
                        IconUrl = PolarsCoreInfo.SelectToken("icon").ToString()
                    });
                }
                return PolarsCoreInfoList;
            }
            else
            {
                return null;
            }
        }

        public async Task<List<PolarsMirrorCoreDetail>> GetCoreDetail(int CoreId)
        {
            HttpResponseMessage Response = await NetworkUtils.SendGetRequest($"{EndPoint}/core/{CoreId}");
            if (Response.IsSuccessStatusCode)
            {
                JToken RemotePolarsCoreDetailList = JsonConvert.DeserializeObject<JToken>(await Response.Content.ReadAsStringAsync());
                List<PolarsMirrorCoreDetail> PolarsCoreDetailList = new();
                foreach (JToken PolarsCoreDetail in RemotePolarsCoreDetailList)
                {
                    PolarsCoreDetailList.Add(new PolarsMirrorCoreDetail
                    {
                        FileName = PolarsCoreDetail.SelectToken("name").ToString(),
                        DownloadUrl = PolarsCoreDetail.SelectToken("downloadUrl").ToString()
                    });
                }
                return PolarsCoreDetailList;
            }
            else
            {
                return null;
            }
        }
        // Unavailable service for now
        //public async Task<List<PolarsServerPackInfo>> GetServerPackInfo()
        //{

        //}
    }
}
