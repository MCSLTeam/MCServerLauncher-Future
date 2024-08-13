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
            public string ModifiedTimeStamp { get; set; }
            public string ForgeVersion { get; set; }
            public List<BmclapiForgeAttachment> Files { get; set; }
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
        private async void FetchMinecraftVersions(object sender, RoutedEventArgs e)
        {
            FetchMinecraftVersionsButton.IsEnabled = false;
            MinecraftVersionComboBox.IsEnabled = false;
            MinecraftVersionComboBox.SelectionChanged -= PreFetchForgeVersions;
            var response = await NetworkUtils.SendGetRequest("https://bmclapi2.bangbang93.com/forge/minecraft");
            MinecraftVersionComboBox.ItemsSource = ResDownloadUtils.SequenceMinecraftVersion(JsonConvert.DeserializeObject<List<string>>(await response.Content.ReadAsStringAsync()));
            MinecraftVersionComboBox.SelectionChanged += PreFetchForgeVersions;
            FetchMinecraftVersionsButton.IsEnabled = true;
            MinecraftVersionComboBox.IsEnabled = true;
        }
        private void PreFetchForgeVersions(object sender, SelectionChangedEventArgs e)
        {
            FetchForgeVersionButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        }
        private async void FetchForgeVersions(object sender, RoutedEventArgs e)
        {
            FetchForgeVersionButton.IsEnabled = false;
            ForgeVersionComboBox.IsEnabled = false;
            var response = await NetworkUtils.SendGetRequest($"https://bmclapi2.bangbang93.com/forge/minecraft/{MinecraftVersionComboBox.SelectedValue}");
            var apiData = JsonConvert.DeserializeObject<JToken>(await response.Content.ReadAsStringAsync());
            CurrentForgeBuilds = apiData!.Select(forgeBuild => new BmclapiForgeBuild
            {
                MinecraftVersion = forgeBuild.SelectToken("mcversion")!.ToString(),
                ModifiedTimeStamp = forgeBuild.SelectToken("modified")!.ToString(),
                ForgeVersion = forgeBuild.SelectToken("version")!.ToString(),
                Files = forgeBuild.SelectToken("files")!.Select(forgeAttachment => new BmclapiForgeAttachment
                {
                    Format = forgeAttachment.SelectToken("format")!.ToString(),
                    Category = forgeAttachment.SelectToken("category")!.ToString(),
                    Hash = forgeAttachment.SelectToken("hash")!.ToString()
                }).ToList()
            }).ToList();
            ForgeVersionComboBox.ItemsSource = ResDownloadUtils.SequenceMinecraftVersion(CurrentForgeBuilds.Select(forgeBuild => forgeBuild.ForgeVersion).ToList());
            ForgeVersionComboBox.IsEnabled = true;
            FetchForgeVersionButton.IsEnabled = true;
        }
    }
}