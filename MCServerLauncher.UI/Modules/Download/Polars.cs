using System.Collections.Generic;
using System.Threading.Tasks;
using System.Net.Http;
using MCServerLauncher.UI.Helpers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MCServerLauncher.UI.Modules.Download
{
    internal class Polars
    {
        private readonly string EndPoint = "https://mirror.polars.cc/api/query/minecraft";

        public class PolarsCoreInfo
        {
            public string Name { get; set; }
            public int Id { get; set; }
            public string Description { get; set; }
        }

        public class PolarsCoreDetail
        {
            public string Name { get; set; }
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

        public async Task<List<PolarsCoreInfo>> GetCoreInfo()
        {
            NetworkUtils NetworkUtils = new();
            HttpResponseMessage Response = await NetworkUtils.SendGetRequest($"{EndPoint}/core");
            if (Response.IsSuccessStatusCode)
            {
                JToken RemotePolarsCoreInfoList = JsonConvert.DeserializeObject<JToken>(await Response.Content.ReadAsStringAsync());
                List<PolarsCoreInfo> PolarsCoreInfoList = new();
                foreach (JToken PolarsCoreInfo in RemotePolarsCoreInfoList)
                {
                    PolarsCoreInfoList.Add(new PolarsCoreInfo
                    {
                        Name = PolarsCoreInfo.SelectToken("name").ToString(),
                        Id = PolarsCoreInfo.SelectToken("id").ToObject<int>(),
                        Description = PolarsCoreInfo.SelectToken("description").ToString()
                    });
                }
                return PolarsCoreInfoList;
            } else {
                return null;
            }
        }

        public async Task<List<PolarsCoreDetail>> GetCoreDetail(int CoreId)
        {
            NetworkUtils NetworkUtils = new();
            HttpResponseMessage Response = await NetworkUtils.SendGetRequest($"{EndPoint}/core/{CoreId}");
            if (Response.IsSuccessStatusCode)
            {
                JToken RemotePolarsCoreDetailList = JsonConvert.DeserializeObject<JToken>(await Response.Content.ReadAsStringAsync());
                List<PolarsCoreDetail> PolarsCoreDetailList = new();
                foreach (JToken PolarsCoreDetail in RemotePolarsCoreDetailList)
                {
                    PolarsCoreDetailList.Add(new PolarsCoreDetail
                    {
                        Name = PolarsCoreDetail.SelectToken("name").ToString(),
                        DownloadUrl = PolarsCoreDetail.SelectToken("downloadUrl").ToString()
                    });
                }
                return PolarsCoreDetailList;
            } else {
                return null;
            }
        }
        //public async Task<List<PolarsServerPackInfo>> GetServerPackInfo()
        //{

        //}
    }
}
