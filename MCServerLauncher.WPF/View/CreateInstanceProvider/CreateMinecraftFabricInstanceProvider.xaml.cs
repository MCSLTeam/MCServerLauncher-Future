using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.WPF.Modules;
using MCServerLauncher.WPF.View.Pages;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls.Primitives;

namespace MCServerLauncher.WPF.View.CreateInstanceProvider
{
    /// <summary>
    ///    CreateMinecraftFabricInstanceProvider.xaml 的交互逻辑
    /// </summary>
    public partial class CreateMinecraftFabricInstanceProvider : ICreateInstanceProvider
    {
        public InstanceType InstanceType { get; } = InstanceType.Fabric;
        public TargetType TargetType { get; } = TargetType.Jar;
        public CreateMinecraftFabricInstanceProvider()
        {
            InitializeComponent();

            ToggleStableMinecraftVersionCheckBox.Checked += ToggleStableMinecraftVersion;
            ToggleStableMinecraftVersionCheckBox.Unchecked += ToggleStableMinecraftVersion;

            ToggleStableFabricVersionCheckBox.Checked += ToggleStableFabricVersion;
            ToggleStableFabricVersionCheckBox.Unchecked += ToggleStableFabricVersion;

            FetchMinecraftVersionsButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            FetchFabricVersionButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        }

        private List<FabricUniversalVersion>? SupportedAllMinecraftVersions { get; set; }
        private List<FabricUniversalVersion>? SupportedAllFabricVersions { get; set; }

        private class FabricUniversalVersion
        {
            public string? Version { get; set; }
            public bool IsStable { get; set; }
        }

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
        ///    Determine the endpoint to fetch data.
        /// </summary>
        /// <returns>The correct endpoint.</returns>
        private string GetEndPoint()
        {
            return SettingsManager.Get?.InstanceCreation != null && SettingsManager.Get.InstanceCreation.UseMirrorForMinecraftFabricInstall
                ? "https://bmclapi2.bangbang93.com/fabric-meta/v2/versions"
                : "https://meta.fabricmc.net/v2/versions";
        }

        /// <summary>
        ///    Fetch supported Minecraft versions.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void FetchMinecraftVersions(object sender, RoutedEventArgs e)
        {
            FetchMinecraftVersionsButton.IsEnabled = false;
            MinecraftVersionComboBox.IsEnabled = false;
            var response = await Network.SendGetRequest($"{GetEndPoint()}/game", true);
            var allSupportedVersionsList = JsonConvert.DeserializeObject<JToken>(await response.Content.ReadAsStringAsync());
            SupportedAllMinecraftVersions = allSupportedVersionsList!.Select(mcVersion => new FabricUniversalVersion
            {
                Version = mcVersion.SelectToken("version")!.ToString(),
                IsStable = mcVersion.SelectToken("stable")!.ToObject<bool>()
            }).ToList();
            ToggleStableMinecraftVersionCheckBox.RaiseEvent(new RoutedEventArgs(ToggleButton.CheckedEvent));
            MinecraftVersionComboBox.IsEnabled = true;
            FetchMinecraftVersionsButton.IsEnabled = true;
        }

        /// <summary>
        ///    Toggle stable/snapshot Minecraft versions.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ToggleStableMinecraftVersion(object sender, RoutedEventArgs e)
        {
            ToggleStableMinecraftVersionCheckBox.IsEnabled = false;
            MinecraftVersionComboBox.IsEnabled = false;
            MinecraftVersionComboBox.ItemsSource = ToggleStableMinecraftVersionCheckBox.IsChecked.GetValueOrDefault(true)
                    ? SupportedAllMinecraftVersions.Where(mcVersion => mcVersion.IsStable).ToList().Select(mcVersion => mcVersion.Version).ToList()
                    : SupportedAllMinecraftVersions.Select(mcVersion => mcVersion.Version).ToList();
            MinecraftVersionComboBox.IsEnabled = true;
            ToggleStableMinecraftVersionCheckBox.IsEnabled = true;
        }

        /// <summary>
        ///    Fetch supported Fabric versions, but not below 0.12.0.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void FetchFabricVersions(object sender, RoutedEventArgs e)
        {
            FetchFabricVersionButton.IsEnabled = false;
            FabricVersionComboBox.IsEnabled = false;
            var response = await Network.SendGetRequest($"{GetEndPoint()}/loader");
            var apiData = JsonConvert.DeserializeObject<JToken>(await response.Content.ReadAsStringAsync());
            SupportedAllFabricVersions = apiData!.Select(mcVersion => new FabricUniversalVersion
            {
                Version = mcVersion.SelectToken("version")!.ToString(),
                IsStable = mcVersion.SelectToken("stable")!.ToObject<bool>()
            }).Where(fabricVersion => fabricVersion.Version != "0.12.0").ToList();
            ToggleStableFabricVersionCheckBox.RaiseEvent(new RoutedEventArgs(ToggleButton.CheckedEvent));
            FabricVersionComboBox.IsEnabled = true;
            FetchFabricVersionButton.IsEnabled = true;
        }

        /// <summary>
        ///    Toggle stable/snapshot Fabric versions.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ToggleStableFabricVersion(object sender, RoutedEventArgs e)
        {
            ToggleStableFabricVersionCheckBox.IsEnabled = false;
            FabricVersionComboBox.IsEnabled = false;
            FabricVersionComboBox.ItemsSource = ToggleStableFabricVersionCheckBox.IsChecked.GetValueOrDefault(true)
                ? SupportedAllFabricVersions.Where(fabricVersion => fabricVersion.IsStable).ToList().Select(mcVersion => mcVersion.Version).ToList()
                : SupportedAllFabricVersions.Select(fabricVersion => fabricVersion.Version).ToList();
            FabricVersionComboBox.IsEnabled = true;
            ToggleStableFabricVersionCheckBox.IsEnabled = true;
        }
    }
}