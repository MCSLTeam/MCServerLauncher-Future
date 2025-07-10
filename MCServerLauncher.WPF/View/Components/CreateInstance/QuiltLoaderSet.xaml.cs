using MCServerLauncher.WPF.Modules;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using static MCServerLauncher.WPF.Modules.Constants;

namespace MCServerLauncher.WPF.View.Components.CreateInstance
{
    /// <summary>
    ///    QuiltLoaderSet.xaml 的交互逻辑
    /// </summary>
    public partial class QuiltLoaderSet : ICreateInstanceStep
    {
        public QuiltLoaderSet()
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
                    SetValue(IsFinished2Property, !(QuiltVersionComboBox.SelectedIndex == -1));
                }
            }

            MinecraftVersionComboBox.SelectionChanged += initialHandler1;
            QuiltVersionComboBox.SelectionChanged += initialHandler2;

            // As you can see, we have to trigger it manually
            VisualTreeHelper.InitStepState(MinecraftVersionComboBox);
            VisualTreeHelper.InitStepState(QuiltVersionComboBox);

            ToggleStableMinecraftVersionCheckBox.Checked += ToggleStableMinecraftVersion;
            ToggleStableMinecraftVersionCheckBox.Unchecked += ToggleStableMinecraftVersion;

            FetchMinecraftVersionsButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            FetchQuiltVersionButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        }

#nullable enable

        private List<QuiltMinecraftVersion>? SupportedAllMinecraftVersions { get; set; }
        private List<string>? QuiltLoaderVersions { get; set; }

        private bool IsDisposed1 { get; set; } = false;
        private bool IsDisposed2 { get; set; } = false;

        ~QuiltLoaderSet()
        {
            IsDisposed1 = true;
            IsDisposed2 = true;
        }

        public static readonly DependencyProperty IsFinished1Property = DependencyProperty.Register(
            nameof(IsFinished1),
            typeof(bool),
            typeof(QuiltLoaderSet),
            new PropertyMetadata(false, OnStatus1Changed));

        private static void OnStatus1Changed(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not QuiltLoaderSet control) return;
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
            typeof(QuiltLoaderSet),
            new PropertyMetadata(false, OnStatus2Changed));

        private static void OnStatus2Changed(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not QuiltLoaderSet control) return;
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
                LoaderVersion = QuiltVersionComboBox.SelectedItem!.ToString(),
            }
        };

        /// <summary>
        ///    Determine the endpoint to fetch data.
        /// </summary>
        /// <returns>The correct endpoint.</returns>
        private string GetEndPoint()
        {
            return SettingsManager.Get?.InstanceCreation != null && SettingsManager.Get.InstanceCreation.UseMirrorForMinecraftQuiltInstall
                ? "https://bmclapi2.bangbang93.com/quilt-meta"
                : "https://meta.quiltmc.org";
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
            var response = await Network.SendGetRequest($"{GetEndPoint()}/v3/versions/game", true);
            var allSupportedVersionsList = JsonConvert.DeserializeObject<JToken>(await response.Content.ReadAsStringAsync());
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
        ///    Toggle stable/snapshot Minecraft versions.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ToggleStableMinecraftVersion(object sender, RoutedEventArgs e)
        {
            ToggleStableMinecraftVersionCheckBox.IsEnabled = false;
            MinecraftVersionComboBox.IsEnabled = false;
            if (SupportedAllMinecraftVersions != null)
                MinecraftVersionComboBox.ItemsSource = DownloadManager.SequenceMinecraftVersion(
                    (ToggleStableMinecraftVersionCheckBox.IsChecked.GetValueOrDefault(true)
                        ? SupportedAllMinecraftVersions.Where(mcVersion => mcVersion.IsStable).ToList()
                            .Select(mcVersion => mcVersion.MinecraftVersion).ToList()
                        : SupportedAllMinecraftVersions.Select(mcVersion => mcVersion.MinecraftVersion).ToList())!
                );
            MinecraftVersionComboBox.IsEnabled = true;
            ToggleStableMinecraftVersionCheckBox.IsEnabled = true;
        }

        /// <summary>
        ///    Fetch supported Quilt versions.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void FetchQuiltVersions(object sender, RoutedEventArgs e)
        {
            FetchQuiltVersionButton.IsEnabled = false;
            QuiltVersionComboBox.IsEnabled = false;
            var response = await Network.SendGetRequest($"{GetEndPoint()}/v3/versions/loader");
            var apiData = JsonConvert.DeserializeObject<JToken>(await response.Content.ReadAsStringAsync());
            QuiltLoaderVersions = apiData!.Select(version => version.SelectToken("version")!.ToString()).ToList();
            QuiltVersionComboBox.ItemsSource = QuiltLoaderVersions;
            QuiltVersionComboBox.IsEnabled = true;
            FetchQuiltVersionButton.IsEnabled = true;
        }

        private class QuiltMinecraftVersion
        {
            public string? MinecraftVersion { get; set; }
            public bool IsStable { get; set; }
        }
#nullable disable
    }
}