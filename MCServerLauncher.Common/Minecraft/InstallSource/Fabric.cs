using MCServerLauncher.Common.Network;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MCServerLauncher.Common.Minecraft.InstallSource
{
    /// <summary>
    ///    Fetch + parse Fabric Minecraft/loader versions from Official or BMCLAPI.
    /// </summary>
    public class Fabric
    {
        /// <summary>
        ///    Get supported Minecraft versions.
        /// </summary>
        /// <param name="useMirror">Use BMCLAPI mirror instead of the official source.</param>
        public async Task<List<FabricUniversalVersion>?> GetMinecraftVersions(bool useMirror)
        {
            var response = await HttpHelper.SendGetRequest($"{GetEndPoint(useMirror)}/game", true);
            var allSupportedVersionsList = JsonConvert.DeserializeObject<JToken>(await response.Content.ReadAsStringAsync());
            return allSupportedVersionsList!.Select(mcVersion => new FabricUniversalVersion
            {
                Version = mcVersion.SelectToken("version")!.ToString(),
                IsStable = mcVersion.SelectToken("stable")!.ToObject<bool>()
            }).ToList();
        }

        /// <summary>
        ///    Get supported Fabric loader versions, but not below 0.12.0.
        /// </summary>
        /// <param name="useMirror">Use BMCLAPI mirror instead of the official source.</param>
        public async Task<List<FabricUniversalVersion>?> GetFabricVersions(bool useMirror)
        {
            var response = await HttpHelper.SendGetRequest($"{GetEndPoint(useMirror)}/loader");
            var apiData = JsonConvert.DeserializeObject<JToken>(await response.Content.ReadAsStringAsync());
            return apiData!.Select(mcVersion => new FabricUniversalVersion
            {
                Version = mcVersion.SelectToken("version")!.ToString(),
                IsStable = mcVersion.SelectToken("stable")!.ToObject<bool>()
            }).Where(fabricVersion => fabricVersion.Version != "0.12.0").ToList();
        }

        private static string GetEndPoint(bool useMirror)
        {
            return useMirror
                ? "https://bmclapi2.bangbang93.com/fabric-meta/v2/versions"
                : "https://meta.fabricmc.net/v2/versions";
        }

        public class FabricUniversalVersion
        {
            public string? Version { get; set; }
            public bool IsStable { get; set; }
        }
    }
}
