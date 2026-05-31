using MCServerLauncher.Common.Network;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace MCServerLauncher.Common.DownloadProvider
{
    public static class MSLAPI
    {
        private const string _endPoint = "https://api.mslmc.cn/v3";

        /// <summary>
        ///    Get core info from MSL API.
        /// </summary>
        /// <returns>List of core name.</returns>
        public static async Task<List<string>?> GetCoreInfo()
        {
            var response = await HttpHelper.SendGetRequest($"{_endPoint}/query/available_server_types");
            if (response.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                return JsonSerializer.Deserialize<List<string>>(doc.RootElement.GetProperty("data").GetProperty("types").GetRawText());
            }
            return null;
        }

        /// <summary>
        ///    Get specific core description from MSL API.
        /// </summary>
        /// <param name="Core">Raw name of the core.</param>
        /// <returns>String of the description.</returns>
        public static async Task<string?> GetCoreDescription(string? Core)
        {
            var response = await HttpHelper.SendGetRequest($"{_endPoint}/query/servers_description/{Core}");
            if (response.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                return doc.RootElement.GetProperty("data").GetProperty("description").GetString();
            }
            return null;
        }

        /// <summary>
        ///    Get Minecraft versions of specific core from MSL API.
        /// </summary>
        /// <param name="core">Raw name of the core.</param>
        /// <returns>List of Minecraft version.</returns>
        public static async Task<List<string>?> GetMinecraftVersions(string? core)
        {
            var response = await HttpHelper.SendGetRequest($"{_endPoint}/query/available_versions/{core}");
            if (!response.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return JsonSerializer.Deserialize<List<string>>(doc.RootElement.GetProperty("data").GetProperty("versionList").GetRawText());
        }

        /// <summary>
        ///    Get download URL of specific file from MSL API.
        /// </summary>
        /// <param name="core">Raw name of the core.</param>
        /// <param name="minecraftVersion">Minecraft version.</param>
        /// <returns>String of the url.</returns>
        public static async Task<string?> GetDownloadUrl(string core, string minecraftVersion)
        {
            var response = await HttpHelper.SendGetRequest($"{_endPoint}/download/server/{core}/{minecraftVersion}");
            if (response.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                return doc.RootElement.GetProperty("data").GetProperty("url").GetString();
            }
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