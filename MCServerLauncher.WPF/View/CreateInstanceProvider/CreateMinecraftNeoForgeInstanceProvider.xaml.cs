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
        // FUCK YOU NeoForged
        private List<BmclapiNeoForgeBuild> CurrentForgeBuilds { get; set; }
        public CreateMinecraftNeoForgeInstanceProvider()
        {
            InitializeComponent();
        }
        private class BmclapiNeoForgeAttachment
        {
            public string Format { get; set; }
            public string Category { get; set; }
            public string Hash { get; set; }
        }
        private class BmclapiNeoForgeBuild
        {
            public string MinecraftVersion { get; set; }
            public string ModifiedTimeStamp { get; set; }
            public string ForgeVersion { get; set; }
            public List<BmclapiNeoForgeAttachment> Files { get; set; }
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
            MinecraftVersionComboBox.SelectionChanged -= PreFetchNeoForgeVersions;
            var response = await NetworkUtils.SendGetRequest("https://bmclapi2.bangbang93.com/forge/minecraft");
            MinecraftVersionComboBox.ItemsSource = ResDownloadUtils.SequenceMinecraftVersion(JsonConvert.DeserializeObject<List<string>>(await response.Content.ReadAsStringAsync()));
            MinecraftVersionComboBox.SelectionChanged += PreFetchNeoForgeVersions;
            FetchMinecraftVersionsButton.IsEnabled = true;
        }
        private void PreFetchNeoForgeVersions(object sender, SelectionChangedEventArgs e)
        {
            FetchNeoForgeVersionButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        }
        private async void FetchNeoForgeVersions(object sender, RoutedEventArgs e)
        {
            var response = await NetworkUtils.SendGetRequest($"https://bmclapi2.bangbang93.com/forge/minecraft/{MinecraftVersionComboBox.SelectedValue}");
            var apiData = JsonConvert.DeserializeObject<JToken>(await response.Content.ReadAsStringAsync());
            CurrentForgeBuilds = apiData!.Select(forgeBuild => new BmclapiNeoForgeBuild
            {
                MinecraftVersion = forgeBuild.SelectToken("mcversion")!.ToString(),
                ModifiedTimeStamp = forgeBuild.SelectToken("modified")!.ToString(),
                ForgeVersion = forgeBuild.SelectToken("version")!.ToString(),
                Files = forgeBuild.SelectToken("files")!.Select(forgeAttachment => new BmclapiNeoForgeAttachment
                {
                    Format = forgeAttachment.SelectToken("format")!.ToString(),
                    Category = forgeAttachment.SelectToken("category")!.ToString(),
                    Hash = forgeAttachment.SelectToken("hash")!.ToString()
                }).ToList()
            }).ToList();

            ForgeVersionComboBox.ItemsSource = ResDownloadUtils.SequenceMinecraftVersion(CurrentForgeBuilds.Select(forgeBuild => forgeBuild.ForgeVersion).ToList());
        }
    }
}