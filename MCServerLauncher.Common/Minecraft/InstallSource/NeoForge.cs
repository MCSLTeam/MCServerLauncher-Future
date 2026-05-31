using MCServerLauncher.Common.Network;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MCServerLauncher.Common.Minecraft.InstallSource
{
    /// <summary>
    ///    Fetch + parse NeoForge data from Official or BMCLAPI.
    /// </summary>
    public class NeoForge
    {
        /// <summary>
        ///    Get NeoForge data, including Minecraft versions and NeoForge versions.
        /// </summary>
        /// <param name="useMirror">Use BMCLAPI mirror instead of the official source.</param>
        public async Task<NeoForgeData> GetData(bool useMirror)
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
            var neoForgeVersions =
                (JsonConvert.DeserializeObject<JToken>(await legacyMavenResponse.Content.ReadAsStringAsync())
                    ?.SelectToken("versions")!.ToObject<List<string>>() ?? throw new InvalidOperationException())
                    .Select(version => version.ToString().Replace("1.20.1-", "")).ToList();
            // Bad version 47.1.82 should be removed
            neoForgeVersions.Remove("47.1.82");
            List<string>? minecraftVersions = null;
            // NeoForge
            var response =
                await HttpHelper.SendGetRequest(
                    "https://maven.neoforged.net/api/maven/versions/releases/net/neoforged/neoforge", true);
            var mavenData =
                JsonConvert.DeserializeObject<JToken>(await response.Content.ReadAsStringAsync())
                    ?.SelectToken("versions")!.ToObject<List<string>>();
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
            var neoForgeVersions =
                JObject.Parse(await legacyMavenResponse.Content.ReadAsStringAsync()).SelectToken("files")!
                    .Select(version => version.SelectToken("name")!.ToString().Replace("1.20.1-", "")).ToList();
            neoForgeVersions.RemoveAll(version => version.Contains("maven-metadata"));
            // NeoForge
            var response = await HttpHelper.SendGetRequest(
                "https://bmclapi2.bangbang93.com/neoforge/meta/api/maven/details/releases/net/neoforged/neoforge",
                true);
            var mavenData = JObject.Parse(await response.Content.ReadAsStringAsync()).SelectToken("files")!
                .Select(version => version.SelectToken("name")!.ToString()).ToList();
            mavenData.RemoveAll(version => version.Contains("maven-metadata"));
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
