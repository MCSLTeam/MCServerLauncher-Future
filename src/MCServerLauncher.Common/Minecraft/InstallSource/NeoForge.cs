using MCServerLauncher.Common.Network;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace MCServerLauncher.Common.Minecraft.InstallSource
{
    /// <summary>
    ///    Fetch + parse NeoForge data from Official or BMCLAPI.
    /// </summary>
    public static class NeoForge
    {
        /// <summary>
        ///    Get NeoForge data, including Minecraft versions and NeoForge versions.
        /// </summary>
        /// <param name="useMirror">Use BMCLAPI mirror instead of the official source.</param>
        public static async Task<NeoForgeData> GetData(bool useMirror)
        {
            return useMirror
                ? await FetchNeoForgeDataByBmclapi()
                : await FetchNeoForgeDataByOfficial();
        }

        private static async Task<NeoForgeData> FetchNeoForgeDataByOfficial()
        {
            // Legacy version (1.20.1)
            var legacyMavenResponse =
                await HttpHelper.SendGetRequest(
                    "https://maven.neoforged.net/api/maven/versions/releases/net/neoforged/forge", true);
            using var legacyDoc = JsonDocument.Parse(await legacyMavenResponse.Content.ReadAsStringAsync());
            var neoForgeVersions =
                (JsonSerializer.Deserialize<List<string>>(legacyDoc.RootElement.GetProperty("versions").GetRawText())
                 ?? throw new InvalidOperationException())
                .Select(version => version.Replace("1.20.1-", "")).ToList();
            // Bad version 47.1.82 should be removed
            neoForgeVersions.Remove("47.1.82");
            List<string>? minecraftVersions = null;
            // NeoForge
            var response =
                await HttpHelper.SendGetRequest(
                    "https://maven.neoforged.net/api/maven/versions/releases/net/neoforged/neoforge", true);
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var mavenData = JsonSerializer.Deserialize<List<string>>(doc.RootElement.GetProperty("versions").GetRawText());
            if (mavenData != null)
            {
                neoForgeVersions.AddRange(mavenData);
                // "1." + the first four digits of mavenData = list of Minecraft versions.
                minecraftVersions = mavenData.Select(version => "1." + version.Substring(0, 4)).Distinct().ToList();
            }

            // Add 1.20.1 to MinecraftVersions
            minecraftVersions?.Add("1.20.1");
            return new NeoForgeData
            {
                NeoForgeVersions = neoForgeVersions,
                MinecraftVersions = minecraftVersions
            };
        }

        private static async Task<NeoForgeData> FetchNeoForgeDataByBmclapi()
        {
            // Legacy version (1.20.1)
            var legacyMavenResponse = await HttpHelper.SendGetRequest(
                "https://bmclapi2.bangbang93.com/neoforge/meta/api/maven/details/releases/net/neoforged/forge", true);
            using var legacyDoc = JsonDocument.Parse(await legacyMavenResponse.Content.ReadAsStringAsync());
            var neoForgeVersions = new List<string>();
            foreach (var file in legacyDoc.RootElement.GetProperty("files").EnumerateArray())
            {
                var name = file.GetProperty("name").GetString()!.Replace("1.20.1-", "");
                if (!name.Contains("maven-metadata"))
                    neoForgeVersions.Add(name);
            }
            // NeoForge
            var response = await HttpHelper.SendGetRequest(
                "https://bmclapi2.bangbang93.com/neoforge/meta/api/maven/details/releases/net/neoforged/neoforge",
                true);
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var mavenData = new List<string>();
            foreach (var file in doc.RootElement.GetProperty("files").EnumerateArray())
            {
                var name = file.GetProperty("name").GetString()!;
                if (!name.Contains("maven-metadata"))
                    mavenData.Add(name);
            }
            neoForgeVersions.AddRange(mavenData);
            // "1." + the first four digits of mavenData = list of Minecraft versions.
            var minecraftVersions = mavenData.Select(version => "1." + version.Substring(0, 4)).Distinct().ToList();
            // Add 1.20.1 to MinecraftVersions
            minecraftVersions.Add("1.20.1");
            return new NeoForgeData
            {
                NeoForgeVersions = neoForgeVersions,
                MinecraftVersions = minecraftVersions
            };
        }

        public class NeoForgeData
        {
            public List<string>? NeoForgeVersions { get; set; }
            public List<string>? MinecraftVersions { get; set; }
        }
    }
}
