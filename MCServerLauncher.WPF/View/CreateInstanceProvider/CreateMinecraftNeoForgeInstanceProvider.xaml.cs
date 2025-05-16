using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.WPF.Modules;
using MCServerLauncher.WPF.View.Pages;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace MCServerLauncher.WPF.View.CreateInstanceProvider
{
    /// <summary>
    ///    CreateMinecraftNeoForgeInstanceProvider.xaml 的交互逻辑
    /// </summary>
    public partial class CreateMinecraftNeoForgeInstanceProvider
    {
        public InstanceType InstanceType { get; } = InstanceType.NeoForge;
        public TargetType TargetType { get; } = TargetType.Jar;
        public CreateMinecraftNeoForgeInstanceProvider()
        {
            InitializeComponent();
            FetchMinecraftVersionsButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        }

        private List<string>? NeoForgeVersions { get; set; }
        private List<string>? MinecraftVersions { get; set; }

        /// <summary>
        ///    Go back.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void GoPreCreateInstance(object sender, RoutedEventArgs e)
        {
            var parent = this.TryFindParent<CreateInstancePage>();
            parent?.CurrentCreateInstance.GoBack();
        }

        //private void FinishSetup(object sender, RoutedEventArgs e)
        //{
        //}

        /// <summary>
        ///    Method to get NeoForge data through Official source.
        /// </summary>
        private async Task FetchNeoForgeDataByOfficial()
        {
            // Legacy version (1.20.1)
            var legacyMavenResponse =
                await Network.SendGetRequest(
                    "https://maven.neoforged.net/api/maven/versions/releases/net/neoforged/forge", true);
            var legacyMavenData =
                (JsonConvert.DeserializeObject<JToken>(await legacyMavenResponse.Content.ReadAsStringAsync())
                    ?.SelectToken("versions")!.ToObject<List<string>>() ?? throw new InvalidOperationException())
                    .Select(version => version.ToString().Replace("1.20.1-", "")).ToList();
            // Bad version 47.1.82 should be removed
            legacyMavenData.Remove("47.1.82");
            NeoForgeVersions = legacyMavenData;
            // NeoForge
            var response =
                await Network.SendGetRequest(
                    "https://maven.neoforged.net/api/maven/versions/releases/net/neoforged/neoforge", true);
            var mavenData =
                JsonConvert.DeserializeObject<JToken>(await response.Content.ReadAsStringAsync())
                    ?.SelectToken("versions")!.ToObject<List<string>>();
            if (mavenData != null)
            {
                NeoForgeVersions.AddRange(mavenData);
                // "1." + the first four digits of mavenData = list of Minecraft versions.
                MinecraftVersions = mavenData.Select(version => "1." + version.Substring(0, 4)).Distinct().ToList();
            }

            // Add 1.20.1 to MinecraftVersions
            MinecraftVersions?.Add("1.20.1");
            // Replace 1.21.0 with 1.21
            MinecraftVersions?.Remove("1.21.0");
            MinecraftVersions?.Add("1.21");
        }

        /// <summary>
        ///    Method to get NeoForge data through BMCLAPI source.
        /// </summary>
        private async Task FetchNeoForgeDataByBmclapi()
        {
            // Legacy version (1.20.1)
            var legacyMavenResponse = await Network.SendGetRequest(
                "https://bmclapi2.bangbang93.com/neoforge/meta/api/maven/details/releases/net/neoforged/forge", true);
            var legacyMavenData =
                JObject.Parse(await legacyMavenResponse.Content.ReadAsStringAsync()).SelectToken("files")!
                    .Select(version => version.SelectToken("name")!.ToString().Replace("1.20.1-", "")).ToList();
            legacyMavenData.RemoveAll(version => version.Contains("maven-metadata"));
            NeoForgeVersions = legacyMavenData;
            // NeoForge
            var response = await Network.SendGetRequest(
                "https://bmclapi2.bangbang93.com/neoforge/meta/api/maven/details/releases/net/neoforged/neoforge",
                true);
            var mavenData = JObject.Parse(await response.Content.ReadAsStringAsync()).SelectToken("files")!
                .Select(version => version.SelectToken("name")!.ToString()).ToList();
            mavenData.RemoveAll(version => version.Contains("maven-metadata"));
            NeoForgeVersions.AddRange(mavenData);
            // "1." + the first four digits of mavenData = list of Minecraft versions.
            MinecraftVersions = mavenData.Select(version => "1." + version.Substring(0, 4)).Distinct().ToList();
            // Add 1.20.1 to MinecraftVersions
            MinecraftVersions.Add("1.20.1");
            // Replace 1.21.0 with 1.21
            MinecraftVersions.Remove("1.21.0");
            MinecraftVersions.Add("1.21");
        }

        /// <summary>
        ///    Get NeoForge info, including Minecraft versions and NeoForge versions.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void FetchNeoForgeData(object sender, RoutedEventArgs e)
        {
            FetchMinecraftVersionsButton.IsEnabled = false;
            MinecraftVersionComboBox.IsEnabled = false;
            MinecraftVersionComboBox.SelectionChanged -= MinecraftVersionChanged;
            NeoForgeVersionComboBox.IsEnabled = false;
            if (SettingsManager.Get?.InstanceCreation != null && SettingsManager.Get.InstanceCreation.UseMirrorForMinecraftNeoForgeInstall)
                await FetchNeoForgeDataByBmclapi();
            else
                await FetchNeoForgeDataByOfficial();
            MinecraftVersionComboBox.ItemsSource = DownloadManager.SequenceMinecraftVersion(MinecraftVersions);
            FetchMinecraftVersionsButton.IsEnabled = true;
            MinecraftVersionComboBox.IsEnabled = true;
            MinecraftVersionComboBox.SelectionChanged += MinecraftVersionChanged;
        }

        /// <summary>
        ///    Reload NeoForge versions.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MinecraftVersionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MinecraftVersionComboBox.SelectedItem == null) return;
            if (MinecraftVersionComboBox.SelectedItem.ToString() == "1.20.1")
            {
                if (NeoForgeVersions != null)
                    NeoForgeVersionComboBox.ItemsSource =
                        DownloadManager.SequenceMinecraftVersion(NeoForgeVersions
                            .Where(version => version.StartsWith("47")).ToList());
                NeoForgeVersionComboBox.IsEnabled = true;
                return;
            }

            if (NeoForgeVersions != null)
                NeoForgeVersionComboBox.ItemsSource = DownloadManager.SequenceMinecraftVersion(NeoForgeVersions
                    .Where(version => version.StartsWith(MinecraftVersionComboBox.SelectedItem.ToString().Substring(2)))
                    .ToList());
            NeoForgeVersionComboBox.IsEnabled = true;
        }
    }
}