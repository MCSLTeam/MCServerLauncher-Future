using iNKORE.UI.WPF.DragDrop.Utilities;
using MCServerLauncher.WPF.Modules;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
    ///    FabricLoaderSet.xaml 的交互逻辑
    /// </summary>
    public partial class FabricLoaderSet : ICreateInstanceStep
    {
        public FabricLoaderSet()
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
                    SetValue(IsFinished2Property, !(FabricVersionComboBox.SelectedIndex == -1));
                }
            }

            MinecraftVersionComboBox.SelectionChanged += initialHandler1;
            FabricVersionComboBox.SelectionChanged += initialHandler2;

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

            FabricVersionComboBox.Items.Add("1");
            FabricVersionComboBox.SelectedIndex = 0;
            FabricVersionComboBox.ClearSelectedItems();
            FabricVersionComboBox.Items.Clear();

            FabricVersionComboBox.SelectionChanged -= initialHandler2;

            FabricVersionComboBox.SelectionChanged += async (sender, args) =>
            {
                await Task.Delay(80);
                if (!IsDisposed2)
                {
                    SetValue(IsFinished2Property, !(FabricVersionComboBox.SelectedIndex == -1));
                }
            };

            ToggleStableMinecraftVersionCheckBox.Checked += ToggleStableMinecraftVersion;
            ToggleStableMinecraftVersionCheckBox.Unchecked += ToggleStableMinecraftVersion;

            ToggleStableFabricVersionCheckBox.Checked += ToggleStableFabricVersion;
            ToggleStableFabricVersionCheckBox.Unchecked += ToggleStableFabricVersion;

            FetchMinecraftVersionsButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        }
#nullable enable
        private List<FabricUniversalVersion>? SupportedAllMinecraftVersions { get; set; }
        private List<FabricUniversalVersion>? SupportedAllFabricVersions { get; set; }

        private class FabricUniversalVersion
        {
            public string? Version { get; set; }
            public bool IsStable { get; set; }
        }

        private bool IsDisposed1 { get; set; } = false;
        private bool IsDisposed2 { get; set; } = false;

        ~FabricLoaderSet()
        {
            IsDisposed1 = true;
            IsDisposed2 = true;
        }

        public static readonly DependencyProperty IsFinished1Property = DependencyProperty.Register(
            nameof(IsFinished1),
            typeof(bool),
            typeof(FabricLoaderSet),
            new PropertyMetadata(false, OnStatus1Changed));

        private static void OnStatus1Changed(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not FabricLoaderSet control) return;
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
            typeof(FabricLoaderSet),
            new PropertyMetadata(false, OnStatus2Changed));

        private static void OnStatus2Changed(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not FabricLoaderSet control) return;
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
                LoaderVersion = FabricVersionComboBox.SelectedItem!.ToString(),
            }
        };

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
            MinecraftVersionComboBox.ClearSelectedItems();
            MinecraftVersionComboBox.SelectionChanged -= PreFetchFabricVersions;
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

        private void PreFetchFabricVersions(object sender, SelectionChangedEventArgs e)
        {
            FetchFabricVersionButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
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
            FabricVersionComboBox.ClearSelectedItems();
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
#nullable disable
    }
}