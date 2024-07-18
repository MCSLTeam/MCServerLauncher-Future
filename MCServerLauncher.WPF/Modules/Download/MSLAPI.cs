using System.Collections.Generic;
using System.Threading.Tasks;
using System.Net.Http;
using MCServerLauncher.WPF.Helpers;
using Newtonsoft.Json;

namespace MCServerLauncher.WPF.Modules.Download
{
    internal class MSLAPI
    {
        private readonly string EndPoint = "https://api.mslmc.cn/v2";

        public async Task<List<string>> GetCoreInfo()
        {
            NetworkUtils NetworkUtils = new();
            HttpResponseMessage Response = await NetworkUtils.SendGetRequest($"{EndPoint}/query/available_server_types");
            if (Response.IsSuccessStatusCode)
            {
                return JsonConvert.DeserializeObject<List<string>>(await Response.Content.ReadAsStringAsync());
            }
            else
            {
                return null;
            }
        }
        public async Task<string> GetCoreDescription(string Core)
        {
            NetworkUtils NetworkUtils = new();
            HttpResponseMessage Response = await NetworkUtils.SendGetRequest($"{EndPoint}/query/servers_description/{Core}");
            if (Response.IsSuccessStatusCode)
            {
                return await Response.Content.ReadAsStringAsync();
            }
            else
            {
                return "获取核心介绍失败！";
            }
        }
        public async Task<List<string>> GetMinecraftVersions(string Core)
        {
            NetworkUtils NetworkUtils = new();
            HttpResponseMessage Response = await NetworkUtils.SendGetRequest($"{EndPoint}/query/available_versions/{Core}");
            if (Response.IsSuccessStatusCode)
            {
                return JsonConvert.DeserializeObject<List<string>>(await Response.Content.ReadAsStringAsync());
            }
            else
            {
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
            }
            else
            {
                return null;
            }
        }
        public string SerializeCoreName(string Core)
        {
            switch (Core)
            {
                case "paper":
                    return "Paper";
                case "purpur":
                    return "Purpur";
                case "leaves":
                    return "Leaves";
                case "spigot":
                    return "Spigot";
                case "arclight":
                    return "Arclight";
                case "arclight-fabric":
                    return "ArclightFabric";
                case "arclight-neoforge":
                    return "ArclightNeoForge";
                case "spongevanilla":
                    return "SpongeVanilla";
                case "mohist":
                    return "Mohist";
                case "catserver":
                    return "CatServer";
                case "banner":
                    return "Banner";
                case "spongeforge":
                    return "SpongeForge";
                case "forge":
                    return "Forge";
                case "neoforge":
                    return "NeoForge";
                case "fabric":
                    return "Fabric";
                case "bukkit":
                    return "Bukkit";
                case "vanilla":
                    return "Vanilla";
                case "folia":
                    return "Folia";
                case "lightfall":
                    return "Lightfall";
                case "pufferfish":
                    return "Pufferfish";
                case "pufferfish_purpur":
                    return "Pufferfish(Purpur)";
                case "pufferfishplus":
                    return "Pufferfish+";
                case "pufferfishplus_purpur":
                    return "Pufferfish+(Purpur)";
                case "travertine":
                    return "Travertine";
                case "bungeecord":
                    return "BungeeCord";
                case "velocity":
                    return "Velocity";
                case "nukkitx":
                    return "NukkitX";
                case "quilt":
                    return "Quilt";
                default:
                    return "Unknown";
            }
        }
    }
}
