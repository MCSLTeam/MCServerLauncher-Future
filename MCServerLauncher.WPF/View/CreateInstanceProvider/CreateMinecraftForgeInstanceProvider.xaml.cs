using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using MCServerLauncher.WPF.Helpers;
using MCServerLauncher.WPF.View.Components;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MCServerLauncher.WPF.View.CreateInstanceProvider
{
    /// <summary>
    ///     CreateMinecraftForgeInstanceProvider.xaml 的交互逻辑
    /// </summary>
    public partial class CreateMinecraftForgeInstanceProvider
    {
        private List<BmclapiForgeBuild> CurrentForgeBuilds { get; set; }
        public CreateMinecraftForgeInstanceProvider()
        {
            InitializeComponent();
            FetchMinecraftVersionsButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        }
        private class BmclapiForgeAttachment
        {
            public string Format { get; set; }
            public string Category { get; set; }
            public string Hash { get; set; }
        }
        private class BmclapiForgeBuild
        {
            public string MinecraftVersion { get; set; }
            public string ForgeVersion { get; set; }
            public List<BmclapiForgeAttachment> Attachments { get; set; }
        }

        private void GoPreCreateInstance(object sender, RoutedEventArgs e)
        {
            var parent = this.TryFindParent<CreateInstancePage>();
            parent.CurrentCreateInstance.GoBack();
        }

        private void FinishSetup(object sender, RoutedEventArgs e)
        {
        }

        private void AddJvmArgument(object sender, RoutedEventArgs e)
        {
            JVMArgumentListView.Items.Add(new JVMArgumentItem());
        }
        /// <summary>
        /// 通过官方获取 Forge 加载器支持的 Minecraft 版本的方法。
        /// </summary>
        private async Task<List<string>> FetchMinecraftVersionsByOfficial()
        {
            var response = await NetworkUtils.SendGetRequest("https://files.minecraftforge.net/maven/net/minecraftforge/forge/index_1.2.4.html", useBrowserUserAgent: true);
            List<string> minecraftVersions = Regex.Matches(await response.Content.ReadAsStringAsync(), "(?<=a href=\"index_)[0-9.]+(_pre[0-9]?)?(?=.html)")
                .Cast<Match>()
                .Select(match => match.Value)
                .ToList();
            minecraftVersions.Add("1.2.4");
            return minecraftVersions;
        }
        /// <summary>
        /// 通过 BMCLAPI 获取 Forge 加载器支持的 Minecraft 版本的方法。
        /// </summary>
        private async Task<List<string>> FetchMinecraftVersionsByBmclapi()
        {
            var response = await NetworkUtils.SendGetRequest("https://bmclapi2.bangbang93.com/forge/minecraft");
            return JsonConvert.DeserializeObject<List<string>>(await response.Content.ReadAsStringAsync());
        }
        /// <summary>
        /// 获取 Forge 加载器支持的 Minecraft 版本的主方法。
        /// </summary>
        private async void FetchMinecraftVersions(object sender, RoutedEventArgs e)
        {
            FetchMinecraftVersionsButton.IsEnabled = false;
            MinecraftVersionComboBox.IsEnabled = false;
            MinecraftVersionComboBox.SelectionChanged -= PreFetchForgeVersions;
            MinecraftVersionComboBox.ItemsSource = ResDownloadUtils.SequenceMinecraftVersion(
                BasicUtils.AppSettings.InstanceCreation.UseMirrorForMinecraftForgeInstall
                ? await FetchMinecraftVersionsByBmclapi()
                : await FetchMinecraftVersionsByOfficial()
            );
            MinecraftVersionComboBox.SelectionChanged += PreFetchForgeVersions;
            FetchMinecraftVersionsButton.IsEnabled = true;
            MinecraftVersionComboBox.IsEnabled = true;
        }
        private void PreFetchForgeVersions(object sender, SelectionChangedEventArgs e)
        {
            FetchForgeVersionButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        }
        /// <summary>
        /// 通过官方获取特定 Minecraft 版本支持的 Forge 加载器版本的方法。
        /// </summary>
        private async Task<List<BmclapiForgeBuild>> FetchForgeVersionsByOfficial(string mcVersion)
        {
            var results = new List<BmclapiForgeBuild>();
            var response = await NetworkUtils.SendGetRequest($"https://files.minecraftforge.net/maven/net/minecraftforge/forge/index_{mcVersion.Replace("-", "_")}.html", useBrowserUserAgent: true);
            var html = await response.Content.ReadAsStringAsync();
            // 分割版本信息
            var versionInfos = html.Substring(0, html.LastIndexOf("</table>", StringComparison.Ordinal)).Split(new[] { "<td class=\"download-version" }, StringSplitOptions.None);
            // 获取所有版本信息
            for (int i = 1; i < versionInfos.Length; i++)
            {
                var forgeVersion = versionInfos[i];
                // 基础信息获取
                string preParsingForgeVersion = RegexSeek(forgeVersion, "(?<=[^(0-9)]+)[0-9\\.]+");
                List<BmclapiForgeAttachment> forgeAttachments = new();
                if (forgeVersion.Contains("classifier-installer\""))
                {
                    // 类型为 installer.jar，支持范围 ~753 (~ 1.6.1 部分), 738~684 (1.5.2 全部)
                    forgeVersion = forgeVersion.Substring(forgeVersion.IndexOf("installer.jar", StringComparison.Ordinal));
                    forgeAttachments.Add(new BmclapiForgeAttachment
                    {
                        Format = "jar",
                        Category = "installer",
                        Hash = RegexSeek(forgeVersion, "(?<=MD5:</strong> )[^<]+")
                    });
                }
                else if (forgeVersion.Contains("classifier-universal\""))
                {
                    // 类型为 universal.zip，支持范围 751~449 (1.6.1 部分), 682~183 (1.5.1 ~ 1.3.2 部分)
                    forgeVersion = forgeVersion.Substring(forgeVersion.IndexOf("universal.zip", StringComparison.Ordinal));
                    forgeAttachments.Add(new BmclapiForgeAttachment
                    {
                        Format = "zip",
                        Category = "universal",
                        Hash = RegexSeek(forgeVersion.Substring(forgeVersion.IndexOf("universal.zip", StringComparison.Ordinal)), "(?<=MD5:</strong> )[^<]+")
                    });
                }
                else if (forgeVersion.Contains("client.zip"))
                {
                    // 类型为 client.zip，支持范围 182~ (1.3.2 部分 ~)
                    forgeVersion = forgeVersion.Substring(forgeVersion.IndexOf("client.zip", StringComparison.Ordinal));
                    forgeAttachments.Add(new BmclapiForgeAttachment
                    {
                        Format = "zip",
                        Category = "client",
                        Hash = RegexSeek(forgeVersion.Substring(forgeVersion.IndexOf("client.zip", StringComparison.Ordinal)), "(?<=MD5:</strong> )[^<]+")
                    });
                }
                else
                {
                    // 没有任何下载（1.6.4 有一部分这种情况）
                    continue;
                }
                results.Add(new BmclapiForgeBuild
                {
                    MinecraftVersion = mcVersion,
                    ForgeVersion = unParsedforgeVersion,
                    Attachments = forgeAttachments
                });
            }
            return results;
        }
        /// <summary>
        /// 通过 BMCLAPI 获取特定 Minecraft 版本支持的 Forge 加载器版本的方法。
        /// </summary>
        private async Task<List<BmclapiForgeBuild>> FetchForgeVersionsByBmclapi(string mcVersion)
        {
            var response = await NetworkUtils.SendGetRequest($"https://bmclapi2.bangbang93.com/forge/minecraft/{mcVersion}");
            var apiData = JsonConvert.DeserializeObject<JToken>(await response.Content.ReadAsStringAsync());
            return apiData!.Select(forgeBuild => new BmclapiForgeBuild
            {
                MinecraftVersion = forgeBuild.SelectToken("mcversion")!.ToString(),
                ForgeVersion = forgeBuild.SelectToken("version")!.ToString(),
                Attachments = forgeBuild.SelectToken("files")!.Select(forgeAttachment => new BmclapiForgeAttachment
                {
                    Format = forgeAttachment.SelectToken("format")!.ToString(),
                    Category = forgeAttachment.SelectToken("category")!.ToString(),
                    Hash = forgeAttachment.SelectToken("hash")!.ToString()
                }).ToList()
            }).ToList();
        }
        /// <summary>
        /// 获取特定 Minecraft 版本支持的 Forge 加载器版本的主方法。
        /// </summary>
        private async void FetchForgeVersions(object sender, RoutedEventArgs e)
        {
            FetchForgeVersionButton.IsEnabled = false;
            ForgeVersionComboBox.IsEnabled = false;
            MinecraftVersionComboBox.IsEnabled = false;
            CurrentForgeBuilds = BasicUtils.AppSettings.InstanceCreation.UseMirrorForMinecraftForgeInstall
                ? await FetchForgeVersionsByBmclapi(MinecraftVersionComboBox.SelectedItem.ToString())
                : await FetchForgeVersionsByOfficial(MinecraftVersionComboBox.SelectedItem.ToString());
            ForgeVersionComboBox.ItemsSource = ResDownloadUtils.SequenceMinecraftVersion(
                CurrentForgeBuilds.Select(forgeBuild => forgeBuild.ForgeVersion).ToList()
            );
            ForgeVersionComboBox.IsEnabled = true;
            FetchForgeVersionButton.IsEnabled = true;
            MinecraftVersionComboBox.IsEnabled = true;
        }
        /// <summary>
        /// Generated from Plain Craft Launcher 2.
        /// </summary>
        private static string RegexSeek(string str, string regex, RegexOptions options = RegexOptions.None)
        {
            var result = Regex.Match(str, regex, options).Value;
            return string.IsNullOrEmpty(result) ? null : result;
        }
    }
}