using System.Collections.Generic;
using System.Threading.Tasks;
using MCServerLauncher.WPF.Main.Helpers;
using Newtonsoft.Json;

namespace MCServerLauncher.WPF.Main.Modules.Download
{
    internal class MSLAPI
    {
        private readonly string _endPoint = "https://api.mslmc.cn/v2";

        public async Task<List<string>> GetCoreInfo()
        {
            var response = await NetworkUtils.SendGetRequest($"{_endPoint}/query/available_server_types");
            if (response.IsSuccessStatusCode)
                return JsonConvert.DeserializeObject<List<string>>(await response.Content.ReadAsStringAsync());
            return null;
        }

        public async Task<string> GetCoreDescription(string Core)
        {
            var response = await NetworkUtils.SendGetRequest($"{_endPoint}/query/servers_description/{Core}");
            if (response.IsSuccessStatusCode)
                return await response.Content.ReadAsStringAsync();
            return "获取核心介绍失败！";
        }

        public async Task<List<string>> GetMinecraftVersions(string core)
        {
            var response = await NetworkUtils.SendGetRequest($"{_endPoint}/query/available_versions/{core}");
            return response.IsSuccessStatusCode
                ? JsonConvert.DeserializeObject<List<string>>(await response.Content.ReadAsStringAsync())
                : null;
        }

        public async Task<string> GetDownloadUrl(string core, string minecraftVersion)
        {
            var response = await NetworkUtils.SendGetRequest($"{_endPoint}/download/server/{core}/{minecraftVersion}");
            if (response.IsSuccessStatusCode)
                return await response.Content.ReadAsStringAsync();
            return null;
        }

        public static string SerializeCoreName(string core)
        {
            return core switch
            {
                "paper" => "Paper",
                "purpur" => "Purpur",
                "leaves" => "Leaves",
                "spigot" => "Spigot",
                "arclight" => "Arclight",
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