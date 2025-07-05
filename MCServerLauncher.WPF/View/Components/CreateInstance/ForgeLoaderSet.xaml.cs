using iNKORE.UI.WPF.DragDrop.Utilities;
using MCServerLauncher.WPF.Modules;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using static MCServerLauncher.WPF.Modules.Constants;

namespace MCServerLauncher.WPF.View.Components.CreateInstance
{
    /// <summary>
    ///    ForgeLoaderSet.xaml 的交互逻辑
    /// </summary>
    public partial class ForgeLoaderSet: ICreateInstanceStep
    {
        public ForgeLoaderSet()
        {
            InitializeComponent();
            void initialHandler1(object sender, SelectionChangedEventArgs args)
            {
                if (!IsDisposed1)
                {
                    SetValue(IsFinished1Property, !(MinecraftVersionComboBox.SelectedIndex == -1));
                }
            }
            void initialHandler2(object sender, SelectionChangedEventArgs args)
            {
                if (!IsDisposed2)
                {
                    SetValue(IsFinished2Property, !(ForgeVersionComboBox.SelectedIndex == -1));
                }
            }

            MinecraftVersionComboBox.SelectionChanged += initialHandler1;
            ForgeVersionComboBox.SelectionChanged += initialHandler2;

            // As you can see, we have to trigger it manually
            MinecraftVersionComboBox.Items.Add("1");
            MinecraftVersionComboBox.SelectedIndex = 0;
            MinecraftVersionComboBox.ClearSelectedItems();
            MinecraftVersionComboBox.Items.Clear();

            MinecraftVersionComboBox.SelectionChanged -= initialHandler2;

            MinecraftVersionComboBox.SelectionChanged += async (sender, args) =>
            {
                await Task.Delay(80);
                if (!IsDisposed1)
                {
                    SetValue(IsFinished1Property, !(MinecraftVersionComboBox.SelectedIndex == -1));
                }
            };

            ForgeVersionComboBox.Items.Add("1");
            ForgeVersionComboBox.SelectedIndex = 0;
            ForgeVersionComboBox.ClearSelectedItems();
            ForgeVersionComboBox.Items.Clear();

            ForgeVersionComboBox.SelectionChanged -= initialHandler2;

            ForgeVersionComboBox.SelectionChanged += async (sender, args) =>
            {
                await Task.Delay(80);
                if (!IsDisposed2)
                {
                    SetValue(IsFinished2Property, !(ForgeVersionComboBox.SelectedIndex == -1));
                }
            };

            FetchMinecraftVersionsButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

        }
#nullable enable
        private List<ForgeBuild>? CurrentForgeBuilds { get; set; }


        private bool IsDisposed1 { get; set; } = false;
        private bool IsDisposed2 { get; set; } = false;

        ~ForgeLoaderSet()
        {
            IsDisposed1 = true;
            IsDisposed2 = true;
        }

        public static readonly DependencyProperty IsFinished1Property = DependencyProperty.Register(
            nameof(IsFinished1),
            typeof(bool),
            typeof(ForgeLoaderSet),
            new PropertyMetadata(false, OnStatus1Changed));

        private static void OnStatus1Changed(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not ForgeLoaderSet control) return;
            if (e.NewValue is not bool status) return;
            control.StatusShow1.Visibility = status switch
            {
                true => Visibility.Visible,
                false => Visibility.Hidden,
            };
        }

        public static readonly DependencyProperty IsFinished2Property = DependencyProperty.Register(
            nameof(IsFinished2),
            typeof(bool),
            typeof(ForgeLoaderSet),
            new PropertyMetadata(false, OnStatus2Changed));

        private static void OnStatus2Changed(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not ForgeLoaderSet control) return;
            if (e.NewValue is not bool status) return;
            control.StatusShow2.Visibility = status switch
            {
                true => Visibility.Visible,
                false => Visibility.Hidden,
            };
        }

        public bool IsFinished1
        {
            get => (bool)GetValue(IsFinished1Property);
            private set => SetValue(IsFinished1Property, value);
        }

        public bool IsFinished2
        {
            get => (bool)GetValue(IsFinished2Property);
            private set => SetValue(IsFinished2Property, value);
        }

        public bool IsFinished
        {
            get => (bool)GetValue(IsFinished1Property) && (bool)GetValue(IsFinished2Property);
            //private set => SetValue(IsFinished1Property, value);
        }

        public CreateInstanceData ActualData => new()
        {
            Type = CreateInstanceDataType.Struct,
            Data = new MinecraftLoaderVersion
            {
                MCVersion = MinecraftVersionComboBox.SelectedItem!.ToString(),
                LoaderVersion = ForgeVersionComboBox.SelectedItem!.ToString(),
            }
        };

        /// <summary>
        ///    Method to get specific version of Minecraft with Forge from Official.
        /// </summary>
        private static async Task<List<string>?> FetchMinecraftVersionsByOfficial()
        {
            var response =
                await Network.SendGetRequest(
                    "https://files.minecraftforge.net/maven/net/minecraftforge/forge/index_1.2.4.html", true);
            var minecraftVersions = Regex.Matches(await response.Content.ReadAsStringAsync(),
                    "(?<=a href=\"index_)[0-9.]+(_pre[0-9]?)?(?=.html)")
                .Cast<Match>()
                .Select(match => match.Value)
                .ToList();
            minecraftVersions.Add("1.2.4");
            return minecraftVersions;
        }

        /// <summary>
        ///    Method to get specific version of Minecraft with Forge from BMCLAPI.
        /// </summary>
        private static async Task<List<string>?> FetchMinecraftVersionsByBmclapi()
        {
            var response = await Network.SendGetRequest("https://bmclapi2.bangbang93.com/forge/minecraft");
            return JsonConvert.DeserializeObject<List<string>>(await response.Content.ReadAsStringAsync());
        }

        /// <summary>
        ///    Main method to get specific version of Minecraft with Forge.
        /// </summary>
        private async void FetchMinecraftVersions(object sender, RoutedEventArgs e)
        {
            FetchMinecraftVersionsButton.IsEnabled = false;
            MinecraftVersionComboBox.IsEnabled = false;
            FetchForgeVersionButton.IsEnabled = false;
            ForgeVersionComboBox.IsEnabled = false;
            MinecraftVersionComboBox.ClearSelectedItems();
            ForgeVersionComboBox.ClearSelectedItems();
            MinecraftVersionComboBox.SelectionChanged -= PreFetchForgeVersions;
            MinecraftVersionComboBox.ItemsSource = DownloadManager.SequenceMinecraftVersion(
                SettingsManager.Get?.InstanceCreation != null && SettingsManager.Get.InstanceCreation.UseMirrorForMinecraftForgeInstall
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
        ///    Method to get version of Forge with a particular Minecraft version from Official.
        /// </summary>
        private static async Task<List<ForgeBuild>?> FetchForgeVersionsByOfficial(string mcVersion)
        {
            var results = new List<ForgeBuild>();
            var response = await Network.SendGetRequest(
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

        /// <summary>
        ///    Method to get version of Forge with a particular Minecraft version from BMCLAPI.
        /// </summary>
        private static async Task<List<ForgeBuild>?> FetchForgeVersionsByBmclapi(string mcVersion)
        {
            var response =
                await Network.SendGetRequest($"https://bmclapi2.bangbang93.com/forge/minecraft/{mcVersion}");
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
        ///    Main method to get version of Forge with a particular Minecraft version.
        /// </summary>
        private async void FetchForgeVersions(object sender, RoutedEventArgs e)
        {
            FetchForgeVersionButton.IsEnabled = false;
            ForgeVersionComboBox.IsEnabled = false;
            MinecraftVersionComboBox.IsEnabled = false;
            ForgeVersionComboBox.ClearSelectedItems();
            CurrentForgeBuilds = SettingsManager.Get?.InstanceCreation != null && SettingsManager.Get.InstanceCreation.UseMirrorForMinecraftForgeInstall
                ? await FetchForgeVersionsByBmclapi(MinecraftVersionComboBox.SelectedItem.ToString())
                : await FetchForgeVersionsByOfficial(MinecraftVersionComboBox.SelectedItem.ToString());
            if (CurrentForgeBuilds != null)
                ForgeVersionComboBox.ItemsSource = DownloadManager.SequenceMinecraftVersion(
                    CurrentForgeBuilds.Select(forgeBuild => forgeBuild.ForgeVersion).ToList()!
                );
            ForgeVersionComboBox.IsEnabled = true;
            FetchForgeVersionButton.IsEnabled = true;
            MinecraftVersionComboBox.IsEnabled = true;
        }

        /// <summary>
        ///    Generated from Plain Craft Launcher 2.
        /// </summary>
        private static string? RegexSeek(string str, string regex, RegexOptions options = RegexOptions.None)
        {
            var result = Regex.Match(str, regex, options).Value;
            return string.IsNullOrEmpty(result) ? null : result;
        }
        private class ForgeAttachment
        {
            public string? Format { get; set; }
            public string? Category { get; set; }
            public string? Hash { get; set; }
        }

        private class ForgeBuild
        {
            public string? MinecraftVersion { get; set; }
            public string? ForgeVersion { get; set; }
            public List<ForgeAttachment>? Attachments { get; set; }
        }
#nullable disable
    }
}