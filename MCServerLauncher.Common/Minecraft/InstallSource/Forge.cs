using MCServerLauncher.Common.Network;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace MCServerLauncher.Common.Minecraft.InstallSource
{
    /// <summary>
    ///    Fetch + parse Forge Minecraft/loader versions from Official or BMCLAPI.
    /// </summary>
    public class Forge
    {
        /// <summary>
        ///    Get supported Minecraft versions.
        /// </summary>
        /// <param name="useMirror">Use BMCLAPI mirror instead of the official source.</param>
        public async Task<List<string>?> GetMinecraftVersions(bool useMirror)
        {
            return useMirror
                ? await FetchMinecraftVersionsByBmclapi()
                : await FetchMinecraftVersionsByOfficial();
        }

        /// <summary>
        ///    Get Forge builds for a particular Minecraft version.
        /// </summary>
        /// <param name="mcVersion">Target Minecraft version.</param>
        /// <param name="useMirror">Use BMCLAPI mirror instead of the official source.</param>
        public async Task<List<ForgeBuild>?> GetForgeVersions(string mcVersion, bool useMirror)
        {
            return useMirror
                ? await FetchForgeVersionsByBmclapi(mcVersion)
                : await FetchForgeVersionsByOfficial(mcVersion);
        }

        private static async Task<List<string>?> FetchMinecraftVersionsByOfficial()
        {
            var response =
                await HttpHelper.SendGetRequest(
                    "https://files.minecraftforge.net/maven/net/minecraftforge/forge/index_1.2.4.html", true);
            var minecraftVersions = Regex.Matches(await response.Content.ReadAsStringAsync(),
                    "(?<=a href=\"index_)[0-9.]+(_pre[0-9]?)?(?=.html)")
                .Cast<Match>()
                .Select(match => match.Value)
                .ToList();
            minecraftVersions.Add("1.2.4");
            return minecraftVersions;
        }

        private static async Task<List<string>?> FetchMinecraftVersionsByBmclapi()
        {
            var response = await HttpHelper.SendGetRequest("https://bmclapi2.bangbang93.com/forge/minecraft");
            return JsonConvert.DeserializeObject<List<string>>(await response.Content.ReadAsStringAsync());
        }

        private static async Task<List<ForgeBuild>?> FetchForgeVersionsByOfficial(string mcVersion)
        {
            var results = new List<ForgeBuild>();
            var response = await HttpHelper.SendGetRequest(
                $"https://files.minecraftforge.net/maven/net/minecraftforge/forge/index_{mcVersion.Replace("-", "_")}.html",
                true);
            var html = await response.Content.ReadAsStringAsync();
            // Split version info
            var versionInfos = html.Substring(0, html.LastIndexOf("</table>", StringComparison.Ordinal))
                .Split(new[] { "<td class=\"download-version" }, StringSplitOptions.None);
            for (var i = 1; i < versionInfos.Length; i++)
            {
                var forgeVersion = versionInfos[i];
                var preParsingForgeVersion = RegexSeek(forgeVersion, "(?<=[^(0-9)]+)[0-9\\.]+");
                List<ForgeAttachment> forgeAttachments = new();
                if (forgeVersion.Contains("classifier-installer\""))
                {
                    // installer.jar，support range: ~753 (~ part of 1.6.1), 738~684 (all of 1.5.2)
                    forgeVersion =
                        forgeVersion.Substring(forgeVersion.IndexOf("installer.jar", StringComparison.Ordinal));
                    forgeAttachments.Add(new ForgeAttachment
                    {
                        Format = "jar",
                        Category = "installer",
                        Hash = RegexSeek(forgeVersion, "(?<=MD5:</strong> )[^<]+")
                    });
                }
                else if (forgeVersion.Contains("classifier-universal\""))
                {
                    // universal.zip，support range: 751~449 (part of 1.6.1), 682~183 (part of 1.5.1 ~ part of 1.3.2)
                    forgeVersion =
                        forgeVersion.Substring(forgeVersion.IndexOf("universal.zip", StringComparison.Ordinal));
                    forgeAttachments.Add(new ForgeAttachment
                    {
                        Format = "zip",
                        Category = "universal",
                        Hash = RegexSeek(
                            forgeVersion.Substring(forgeVersion.IndexOf("universal.zip", StringComparison.Ordinal)),
                            "(?<=MD5:</strong> )[^<]+")
                    });
                }
                else if (forgeVersion.Contains("client.zip"))
                {
                    // client.zip，support range: 182~ (part of 1.3.2 ~)
                    forgeVersion = forgeVersion.Substring(forgeVersion.IndexOf("client.zip", StringComparison.Ordinal));
                    forgeAttachments.Add(new ForgeAttachment
                    {
                        Format = "zip",
                        Category = "client",
                        Hash = RegexSeek(
                            forgeVersion.Substring(forgeVersion.IndexOf("client.zip", StringComparison.Ordinal)),
                            "(?<=MD5:</strong> )[^<]+")
                    });
                }
                else
                {
                    // Empty (part of 1.6.4)
                    continue;
                }

                results.Add(new ForgeBuild
                {
                    MinecraftVersion = mcVersion,
                    ForgeVersion = preParsingForgeVersion,
                    Attachments = forgeAttachments
                });
            }

            return results;
        }

        private static async Task<List<ForgeBuild>?> FetchForgeVersionsByBmclapi(string mcVersion)
        {
            var response =
                await HttpHelper.SendGetRequest($"https://bmclapi2.bangbang93.com/forge/minecraft/{mcVersion}");
            var apiData = JsonConvert.DeserializeObject<JToken>(await response.Content.ReadAsStringAsync());
            return apiData!.Select(forgeBuild => new ForgeBuild
            {
                MinecraftVersion = forgeBuild.SelectToken("mcversion")!.ToString(),
                ForgeVersion = forgeBuild.SelectToken("version")!.ToString(),
                Attachments = forgeBuild.SelectToken("files")!.Select(forgeAttachment => new ForgeAttachment
                {
                    Format = forgeAttachment.SelectToken("format")!.ToString(),
                    Category = forgeAttachment.SelectToken("category")!.ToString(),
                    Hash = forgeAttachment.SelectToken("hash")!.ToString()
                }).ToList()
            }).ToList();
        }

        /// <summary>
        ///    Generated from Plain Craft Launcher 2.
        /// </summary>
        private static string? RegexSeek(string str, string regex, RegexOptions options = RegexOptions.None)
        {
            var result = Regex.Match(str, regex, options).Value;
            return string.IsNullOrEmpty(result) ? null : result;
        }

        public class ForgeAttachment
        {
            public string? Format { get; set; }
            public string? Category { get; set; }
            public string? Hash { get; set; }
        }

        public class ForgeBuild
        {
            public string? MinecraftVersion { get; set; }
            public string? ForgeVersion { get; set; }
            public List<ForgeAttachment>? Attachments { get; set; }
        }
    }
}
