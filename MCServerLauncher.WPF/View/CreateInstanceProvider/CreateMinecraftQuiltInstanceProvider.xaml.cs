using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using iNKORE.UI.WPF.Modern.Controls;
using MCServerLauncher.WPF.Helpers;
using MCServerLauncher.WPF.View.Components;
using MCServerLauncher.WPF.View.Pages;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MCServerLauncher.WPF.View.CreateInstanceProvider
{
    /// <summary>
    ///     CreateMinecraftQuiltInstanceProvider.xaml 的交互逻辑
    /// </summary>
    public partial class CreateMinecraftQuiltInstanceProvider
    {
        private List<QuiltMinecraftVersion> SupportedAllMinecraftVersions { get; set; }
        private List<string> QuiltLoaderVersions { get; set; }
        public CreateMinecraftQuiltInstanceProvider()
        {
            InitializeComponent();
            ToggleStableMinecraftVersionCheckBox.Checked += ToggleStableMinecraftVersion;
            FetchMinecraftVersionsButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            FetchQuiltVersionButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        }
        private class QuiltMinecraftVersion
        {
            public string MinecraftVersion { get; set; }
            public bool IsStable { get; set; }
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
        /// Determine the endpoint to fetch data.
        /// </summary>
        /// <returns>The correct endpoint.</returns>
        private string GetEndPoint()
        {
            return BasicUtils.AppSettings.InstanceCreation.UseMirrorForMinecraftQuiltInstall
                ? "https://bmclapi2.bangbang93.com/quilt-meta"
                : "https://meta.quiltmc.org";
        }

        /// <summary>
        /// Fetch supported Minecraft versions.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void FetchMinecraftVersions(object sender, RoutedEventArgs e)
        {
            FetchMinecraftVersionsButton.IsEnabled = false;
            MinecraftVersionComboBox.IsEnabled = false;
            var response = await NetworkUtils.SendGetRequest($"{GetEndPoint()}/v3/versions/game", useBrowserUserAgent: true);
            var content = await response.Content.ReadAsStringAsync();
            var allSupportedVersionsList = JsonConvert.DeserializeObject<JToken>(content);
            SupportedAllMinecraftVersions = allSupportedVersionsList!.Select(mcVersion => new QuiltMinecraftVersion
            {
                MinecraftVersion = mcVersion.SelectToken("version")!.ToString(),
                IsStable = mcVersion.SelectToken("stable")!.ToObject<bool>()
            }).ToList();
            ToggleStableMinecraftVersionCheckBox.RaiseEvent(new RoutedEventArgs(ToggleButton.CheckedEvent));
            MinecraftVersionComboBox.IsEnabled = true;
            FetchMinecraftVersionsButton.IsEnabled = true;
        }
        /// <summary>
        /// Toggle stable/snapshot Minecraft version.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ToggleStableMinecraftVersion(object sender, RoutedEventArgs e)
        {
            ToggleStableMinecraftVersionCheckBox.IsEnabled = false;
            MinecraftVersionComboBox.IsEnabled = false;
            MinecraftVersionComboBox.ItemsSource = ResDownloadUtils.SequenceMinecraftVersion(
                ToggleStableMinecraftVersionCheckBox.IsChecked.GetValueOrDefault(true) ?
                SupportedAllMinecraftVersions.Where(mcVersion => mcVersion.IsStable).ToList().Select(mcVersion => mcVersion.MinecraftVersion).ToList() :
                SupportedAllMinecraftVersions.Select(mcVersion => mcVersion.MinecraftVersion).ToList()
            );
            MinecraftVersionComboBox.IsEnabled = true;
            ToggleStableMinecraftVersionCheckBox.IsEnabled = true;
        }

        /// <summary>
        /// Fetch supported Quilt versions.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void FetchQuiltVersions(object sender, RoutedEventArgs e)
        {
            FetchQuiltVersionButton.IsEnabled = false;
            QuiltVersionComboBox.IsEnabled = false;
            var response = await NetworkUtils.SendGetRequest($"{GetEndPoint()}/v3/versions/loader");
            var apiData = JsonConvert.DeserializeObject<JToken>(await response.Content.ReadAsStringAsync());
            QuiltLoaderVersions = apiData!.Select(version => version.SelectToken("version")!.ToString()).ToList();
            QuiltVersionComboBox.ItemsSource = QuiltLoaderVersions;
            QuiltVersionComboBox.IsEnabled = true;
            FetchQuiltVersionButton.IsEnabled = true;
        }
    }
}