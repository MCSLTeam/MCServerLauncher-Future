using MCServerLauncher.WPF.Modules;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using static MCServerLauncher.WPF.Modules.Constants;

namespace MCServerLauncher.WPF.View.Components.CreateInstance
{
    /// <summary>
    ///    NeoForgeLoaderSet.xaml 的交互逻辑
    /// </summary>
    public partial class NeoForgeLoaderSet : ICreateInstanceStep
    {
        public NeoForgeLoaderSet()
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
                    SetValue(IsFinished2Property, !(NeoForgeVersionComboBox.SelectedIndex == -1));
                }
            }

            MinecraftVersionComboBox.SelectionChanged += initialHandler1;
            NeoForgeVersionComboBox.SelectionChanged += initialHandler2;

            // As you can see, we have to trigger it manually
            VisualTreeHelper.InitStepState(MinecraftVersionComboBox);
            VisualTreeHelper.InitStepState(NeoForgeVersionComboBox);

            FetchMinecraftVersionsButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        }

#nullable enable
        private List<string>? NeoForgeVersions { get; set; }
        private List<string>? MinecraftVersions { get; set; }

        private bool IsDisposed1 { get; set; } = false;
        private bool IsDisposed2 { get; set; } = false;

        ~NeoForgeLoaderSet()
        {
            IsDisposed1 = true;
            IsDisposed2 = true;
        }

        public static readonly DependencyProperty IsFinished1Property = DependencyProperty.Register(
            nameof(IsFinished1),
            typeof(bool),
            typeof(NeoForgeLoaderSet),
            new PropertyMetadata(false, OnStatus1Changed));

        private static void OnStatus1Changed(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not NeoForgeLoaderSet control) return;
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
            typeof(NeoForgeLoaderSet),
            new PropertyMetadata(false, OnStatus2Changed));

        private static void OnStatus2Changed(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not NeoForgeLoaderSet control) return;
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
                LoaderVersion = NeoForgeVersionComboBox.SelectedItem!.ToString(),
            }
        };

        /// <summary>
        ///    Method to get NeoForge data from Official.
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
        }

        /// <summary>
        ///    Method to get NeoForge data from BMCLAPI.
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
        }

        /// <summary>
        ///    Get NeoForge info, including Minecraft versions and NeoForge versions.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void FetchNeoForgeData(object sender, RoutedEventArgs e)
        {
            VisualTreeHelper.InitStepState(MinecraftVersionComboBox);
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
#nullable disable
    }
}