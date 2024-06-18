using System.Collections.Generic;
using System.Threading.Tasks;
using System.Net.Http;
using MCServerLauncher.UI.Helpers;
using Newtonsoft.Json;

namespace MCServerLauncher.UI.Modules.Download
{
    internal class MSL
    {
        private readonly string EndPoint = "https://api.mslmc.cn/v2";

        public async Task<List<string>> GetCoreInfo()
        {
            NetworkUtils NetworkUtils = new();
            HttpResponseMessage Response = await NetworkUtils.SendGetRequest($"{EndPoint}/query/available_server_types");
            if (Response.IsSuccessStatusCode)
            {
                return JsonConvert.DeserializeObject<List<string>>(await Response.Content.ReadAsStringAsync());
            } else {
                return null;
            }
        }
        public async Task<List<string>> GetMinecraftVersions(string Core)
        {
            NetworkUtils NetworkUtils = new();
            HttpResponseMessage Response = await NetworkUtils.SendGetRequest($"{EndPoint}/query/available_versions/{Core}");
            if (Response.IsSuccessStatusCode)
            {
                return JsonConvert.DeserializeObject<List<string>>(await Response.Content.ReadAsStringAsync());
            } else {
                return null;
            }
        }

        public async Task<string> GetDownloadUrl(string Core, string MinecraftVersion)
            {
            NetworkUtils NetworkUtils = new();
            HttpResponseMessage Response = await NetworkUtils.SendGetRequest($"{EndPoint}/download/server/{Core}/{MinecraftVersion}");
            if (Response.IsSuccessStatusCode)
            {
                return await Response.Content.ReadAsStringAsync();
            } else {
                return null;
            }
        }
    }
}
