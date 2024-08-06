using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MCServerLauncher.WPF.Helpers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MCServerLauncher.WPF.Modules.Download
{
    internal class PolarsMirror
    {
        private readonly string _endPoint = "https://mirror.polars.cc/api/query/minecraft";

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
            var response = await NetworkUtils.SendGetRequest($"{_endPoint}/core");
            if (!response.IsSuccessStatusCode) return null;
            var remotePolarsCoreInfoList =
                JsonConvert.DeserializeObject<JToken>(await response.Content.ReadAsStringAsync());
            return remotePolarsCoreInfoList.Select(polarsCoreInfo => new PolarsMirrorCoreInfo
            {
                Name = polarsCoreInfo.SelectToken("name")?.ToString(),
                Id = polarsCoreInfo.SelectToken("id")!.ToObject<int>(),
                Description = polarsCoreInfo.SelectToken("description")?.ToString(),
                IconUrl = polarsCoreInfo.SelectToken("icon")?.ToString()
            }).ToList();
        }

        public async Task<List<PolarsMirrorCoreDetail>> GetCoreDetail(int coreId)
        {
            var response = await NetworkUtils.SendGetRequest($"{_endPoint}/core/{coreId}");
            if (!response.IsSuccessStatusCode) return null;
            var remotePolarsCoreDetailList =
                JsonConvert.DeserializeObject<JToken>(await response.Content.ReadAsStringAsync());
            return remotePolarsCoreDetailList.Select(polarsCoreDetail => new PolarsMirrorCoreDetail
            {
                FileName = polarsCoreDetail.SelectToken("name")?.ToString(),
                DownloadUrl = polarsCoreDetail.SelectToken("downloadUrl")?.ToString()
            }).ToList();
        }

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
        //public async Task<List<PolarsServerPackInfo>> GetServerPackInfo()
        //{

        //}
    }
}