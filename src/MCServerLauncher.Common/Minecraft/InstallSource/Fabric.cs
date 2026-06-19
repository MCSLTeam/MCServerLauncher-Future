using MCServerLauncher.Common.Network;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace MCServerLauncher.Common.Minecraft.InstallSource
{
    /// <summary>
    ///    Fetch + parse Fabric Minecraft/loader versions from Official or BMCLAPI.
    /// </summary>
    public static class Fabric
    {
        /// <summary>
        ///    Get supported Minecraft versions.
        /// </summary>
        /// <param name="useMirror">Use BMCLAPI mirror instead of the official source.</param>
        public static async Task<List<FabricUniversalVersion>?> GetMinecraftVersions(bool useMirror)
        {
            var response = await HttpHelper.SendGetRequest($"{GetEndPoint(useMirror)}/game", true);
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var results = new List<FabricUniversalVersion>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                results.Add(new FabricUniversalVersion
                {
                    Version = item.GetProperty("version").GetString(),
                    IsStable = item.GetProperty("stable").GetBoolean()
                });
            }
            return results;
        }

        /// <summary>
        ///    Get supported Fabric loader versions, but not below 0.12.0.
        /// </summary>
        /// <param name="useMirror">Use BMCLAPI mirror instead of the official source.</param>
        public static async Task<List<FabricUniversalVersion>?> GetFabricVersions(bool useMirror)
        {
            var response = await HttpHelper.SendGetRequest($"{GetEndPoint(useMirror)}/loader");
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var results = new List<FabricUniversalVersion>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var version = new FabricUniversalVersion
                {
                    Version = item.GetProperty("version").GetString(),
                    IsStable = item.GetProperty("stable").GetBoolean()
                };
                if (version.Version != "0.12.0")
                    results.Add(version);
            }
            return results;
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
