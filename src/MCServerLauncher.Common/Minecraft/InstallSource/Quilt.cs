using MCServerLauncher.Common.Network;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace MCServerLauncher.Common.Minecraft.InstallSource
{
    /// <summary>
    ///    Fetch + parse Quilt Minecraft/loader versions from Official or BMCLAPI.
    /// </summary>
    public static class Quilt
    {
        /// <summary>
        ///    Get supported Minecraft versions.
        /// </summary>
        /// <param name="useMirror">Use BMCLAPI mirror instead of the official source.</param>
        public static async Task<List<QuiltMinecraftVersion>?> GetMinecraftVersions(bool useMirror)
        {
            var response = await HttpHelper.SendGetRequest($"{GetEndPoint(useMirror)}/v3/versions/game", true);
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var results = new List<QuiltMinecraftVersion>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                results.Add(new QuiltMinecraftVersion
                {
                    MinecraftVersion = item.GetProperty("version").GetString(),
                    IsStable = item.GetProperty("stable").GetBoolean()
                });
            }
            return results;
        }

        /// <summary>
        ///    Get supported Quilt loader versions.
        /// </summary>
        /// <param name="useMirror">Use BMCLAPI mirror instead of the official source.</param>
        public static async Task<List<string>?> GetQuiltVersions(bool useMirror)
        {
            var response = await HttpHelper.SendGetRequest($"{GetEndPoint(useMirror)}/v3/versions/loader");
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var results = new List<string>();
            foreach (var item in doc.RootElement.EnumerateArray())
                results.Add(item.GetProperty("version").GetString()!);
            return results;
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
