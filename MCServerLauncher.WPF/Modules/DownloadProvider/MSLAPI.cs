using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MCServerLauncher.WPF.Modules.DownloadProvider
{
    internal class MSLAPI
    {
        private readonly string _endPoint = "https://api.mslmc.cn/v3";

        /// <summary>
        ///    Get core info from MSL API.
        /// </summary>
        /// <returns>List of core name.</returns>
        public async Task<List<string>> GetCoreInfo()
        {
            var response = await Network.SendGetRequest($"{_endPoint}/query/available_server_types");
            if (response.IsSuccessStatusCode)
                return JsonConvert.DeserializeObject<JToken>(await response.Content.ReadAsStringAsync())
                    .SelectToken("data")!.SelectToken("types")!.ToObject<List<string>>();
            return null;
        }

        /// <summary>
        ///    Get specific core description from MSL API.
        /// </summary>
        /// <param name="Core">Raw name of the core.</param>
        /// <returns>String of the description.</returns>
        public async Task<string> GetCoreDescription(string Core)
        {
            var response = await Network.SendGetRequest($"{_endPoint}/query/servers_description/{Core}");
            if (response.IsSuccessStatusCode)
                return JsonConvert.DeserializeObject<JToken>(await response.Content.ReadAsStringAsync())
                    .SelectToken("data")!.SelectToken("description")!.ToString();
            return LanguageManager.Localize["DownloadModule_MSLAPIGetCoreDescriptionFailed"];
        }

        /// <summary>
        ///    Get Minecraft versions of specific core from MSL API.
        /// </summary>
        /// <param name="core">Raw name of the core.</param>
        /// <returns>List of Minecraft version.</returns>
        public async Task<List<string>> GetMinecraftVersions(string core)
        {
            var response = await Network.SendGetRequest($"{_endPoint}/query/available_versions/{core}");
            return response.IsSuccessStatusCode
                ? JsonConvert.DeserializeObject<JToken>(await response.Content.ReadAsStringAsync()).SelectToken("data")!
                    .SelectToken("versionList")!.ToObject<List<string>>()
                : null;
        }

        /// <summary>
        ///    Get download URL of specific file from MSL API.
        /// </summary>
        /// <param name="core">Raw name of the core.</param>
        /// <param name="minecraftVersion">Minecraft version.</param>
        /// <returns>String of the url.</returns>
        public async Task<string> GetDownloadUrl(string core, string minecraftVersion)
        {
            var response = await Network.SendGetRequest($"{_endPoint}/download/server/{core}/{minecraftVersion}");
            if (response.IsSuccessStatusCode)
                return JsonConvert.DeserializeObject<JToken>(await response.Content.ReadAsStringAsync())
                    .SelectToken("data")!.SelectToken("url")!.ToString();
            return null;
        }

        /// <summary>
        ///    Prettier the core name.
        /// </summary>
        /// <param name="core"></param>
        /// <returns></returns>
        public static string SerializeCoreName(string core)
        {
            return core switch
            {
                "paper" => "Paper",
                "purpur" => "Purpur",
                "leaves" => "Leaves",
                "spigot" => "Spigot",
                "arclight" => "Arclight",
                "arclight-forge" => "ArclightForge",
                "arclight-fabric" => "ArclightFabric",
                "arclight-neoforge" => "ArclightNeoForge",
                "spongevanilla" => "SpongeVanilla",
                "mohist" => "Mohist",
                "catserver" => "CatServer",
                "banner" => "Banner",
                "spongeforge" => "SpongeForge",
                "forge" => "Forge",
                "neoforge" => "NeoForge",
                "fabric" => "Fabric",
                "bukkit" => "Bukkit",
                "vanilla" => "Vanilla",
                "folia" => "Folia",
                "lightfall" => "Lightfall",
                "pufferfish" => "Pufferfish",
                "pufferfish_purpur" => "Pufferfish(Purpur)",
                "pufferfishplus" => "Pufferfish+",
                "pufferfishplus_purpur" => "Pufferfish+(Purpur)",
                "travertine" => "Travertine",
                "bungeecord" => "BungeeCord",
                "velocity" => "Velocity",
                "nukkitx" => "NukkitX",
                "quilt" => "Quilt",
                _ => "Unknown"
            };
        }
    }
}