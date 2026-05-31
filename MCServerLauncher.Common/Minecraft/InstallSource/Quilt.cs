using MCServerLauncher.Common.Network;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MCServerLauncher.Common.Minecraft.InstallSource
{
    /// <summary>
    ///    Fetch + parse Quilt Minecraft/loader versions from Official or BMCLAPI.
    /// </summary>
    public class Quilt
    {
        /// <summary>
        ///    Get supported Minecraft versions.
        /// </summary>
        /// <param name="useMirror">Use BMCLAPI mirror instead of the official source.</param>
        public async Task<List<QuiltMinecraftVersion>?> GetMinecraftVersions(bool useMirror)
        {
            var response = await HttpHelper.SendGetRequest($"{GetEndPoint(useMirror)}/v3/versions/game", true);
            var allSupportedVersionsList = JsonConvert.DeserializeObject<JToken>(await response.Content.ReadAsStringAsync());
            return allSupportedVersionsList!.Select(mcVersion => new QuiltMinecraftVersion
            {
                MinecraftVersion = mcVersion.SelectToken("version")!.ToString(),
                IsStable = mcVersion.SelectToken("stable")!.ToObject<bool>()
            }).ToList();
        }

        /// <summary>
        ///    Get supported Quilt loader versions.
        /// </summary>
        /// <param name="useMirror">Use BMCLAPI mirror instead of the official source.</param>
        public async Task<List<string>?> GetQuiltVersions(bool useMirror)
        {
            var response = await HttpHelper.SendGetRequest($"{GetEndPoint(useMirror)}/v3/versions/loader");
            var apiData = JsonConvert.DeserializeObject<JToken>(await response.Content.ReadAsStringAsync());
            return apiData!.Select(version => version.SelectToken("version")!.ToString()).ToList();
        }

        private static string GetEndPoint(bool useMirror)
        {
            return useMirror
                ? "https://bmclapi2.bangbang93.com/quilt-meta"
                : "https://meta.quiltmc.org";
        }

        public class QuiltMinecraftVersion
        {
            public string? MinecraftVersion { get; set; }
            public bool IsStable { get; set; }
        }
    }
}
