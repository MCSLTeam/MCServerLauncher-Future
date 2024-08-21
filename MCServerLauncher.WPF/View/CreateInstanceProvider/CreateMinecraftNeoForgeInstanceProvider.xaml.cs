using System.Collections.Generic;
using System.Linq;
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
    ///     CreateMinecraftNeoForgeInstanceProvider.xaml 的交互逻辑
    /// </summary>
    public partial class CreateMinecraftNeoForgeInstanceProvider
    {
        private List<string> NeoForgeVersions { get; set; }
        private List<string> MinecraftVersions { get; set; }
        public CreateMinecraftNeoForgeInstanceProvider()
        {
            InitializeComponent();
            FetchMinecraftVersionsButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        }

        /// <summary>
        /// Go back.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
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
        /// Get NeoForge info, including Minecraft versions and NeoForge versions.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void FetchNeoForgeData(object sender, RoutedEventArgs e)
        {
            FetchMinecraftVersionsButton.IsEnabled = false;
            MinecraftVersionComboBox.IsEnabled = false;
            MinecraftVersionComboBox.SelectionChanged -= MinecraftVersionChanged;
            NeoForgeVersionComboBox.IsEnabled = false;
            // Legacy version (1.20.1)
            var legacyMavenResponse = await NetworkUtils.SendGetRequest("https://bmclapi2.bangbang93.com/neoforge/meta/api/maven/details/releases/net/neoforged/forge", useBrowserUserAgent: true);
            var legacyMavenData = JObject.Parse(await legacyMavenResponse.Content.ReadAsStringAsync()).SelectToken("files")!.Select(version => version.SelectToken("name")!.ToString().Replace("1.20.1-", "")).ToList();
            legacyMavenData.RemoveAll(version => version.Contains("maven-metadata"));
            NeoForgeVersions = legacyMavenData;
            // NeoForge
            var response = await NetworkUtils.SendGetRequest("https://bmclapi2.bangbang93.com/neoforge/meta/api/maven/details/releases/net/neoforged/neoforge", useBrowserUserAgent: true);
            var mavenData = JObject.Parse(await response.Content.ReadAsStringAsync()).SelectToken("files")!.Select(version => version.SelectToken("name")!.ToString()).ToList();
            mavenData.RemoveAll(version => version.Contains("maven-metadata"));
            NeoForgeVersions.AddRange(mavenData);
            // 1.前缀 加 mavenData前四位整理成 Minecraft 版本列表
            MinecraftVersions = mavenData.Select(version => "1." + version.Substring(0, 4)).Distinct().ToList();
            // 1.20.1 版本加到 Minecraft 版本列表
            MinecraftVersions.Add("1.20.1");
            // 1.21.0 替换为 1.21
            MinecraftVersions.Remove("1.21.0");
            MinecraftVersions.Add("1.21");
            MinecraftVersions = ResDownloadUtils.SequenceMinecraftVersion(MinecraftVersions);
            MinecraftVersionComboBox.ItemsSource = MinecraftVersions;
            FetchMinecraftVersionsButton.IsEnabled = true;
            MinecraftVersionComboBox.IsEnabled = true;
            MinecraftVersionComboBox.SelectionChanged += MinecraftVersionChanged;
        }

        /// <summary>
        /// Reload NeoForge versions.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MinecraftVersionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MinecraftVersionComboBox.SelectedItem == null) return;
            if (MinecraftVersionComboBox.SelectedItem.ToString() == "1.20.1")
            {
                NeoForgeVersionComboBox.ItemsSource = ResDownloadUtils.SequenceMinecraftVersion(NeoForgeVersions.Where(version => version.StartsWith("47")).ToList());
                NeoForgeVersionComboBox.IsEnabled = true;
                return;
            }
            NeoForgeVersionComboBox.ItemsSource = ResDownloadUtils.SequenceMinecraftVersion(NeoForgeVersions.Where(version => version.StartsWith(MinecraftVersionComboBox.SelectedItem.ToString().Substring(2))).ToList());
            NeoForgeVersionComboBox.IsEnabled = true;
        }
    }
}