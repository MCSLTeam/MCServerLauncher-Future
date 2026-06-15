using MCServerLauncher.Common.Minecraft.InstallSource;
using MCServerLauncher.WPF.Modules;
using System.Collections.Generic;
using System.Linq;
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
            LoaderSetStepHelper.BindSelectionStatus(this, MinecraftVersionComboBox, IsFinished1Property);
            LoaderSetStepHelper.BindSelectionStatus(this, NeoForgeVersionComboBox, IsFinished2Property);

            FetchMinecraftVersionsButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        }

        private List<string>? NeoForgeVersions { get; set; }
        private List<string>? MinecraftVersions { get; set; }

        public static readonly DependencyProperty IsFinished1Property = DependencyProperty.Register(
            nameof(IsFinished1),
            typeof(bool),
            typeof(NeoForgeLoaderSet),
            new PropertyMetadata(false,
                LoaderSetStepHelper.CreateStatusVisibilityCallback<NeoForgeLoaderSet>(control => control.StatusShow1)));

        public static readonly DependencyProperty IsFinished2Property = DependencyProperty.Register(
            nameof(IsFinished2),
            typeof(bool),
            typeof(NeoForgeLoaderSet),
            new PropertyMetadata(false,
                LoaderSetStepHelper.CreateStatusVisibilityCallback<NeoForgeLoaderSet>(control => control.StatusShow2)));

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

        public CreateInstanceData ActualData
        {
            get
            {
                return LoaderSetStepHelper.CreateLoaderVersionData(
                    MinecraftVersionComboBox,
                    NeoForgeVersionComboBox,
                    "NeoForge");
            }
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
            var useMirror = SettingsManager.Get?.InstanceCreation != null &&
                            SettingsManager.Get.InstanceCreation.UseMirrorForMinecraftNeoForgeInstall;
            var neoForgeData = await NeoForge.GetData(useMirror);
            NeoForgeVersions = neoForgeData.NeoForgeVersions;
            MinecraftVersions = neoForgeData.MinecraftVersions;
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
            var selectedMinecraftVersion = MinecraftVersionComboBox.SelectedItem?.ToString();
            if (string.IsNullOrWhiteSpace(selectedMinecraftVersion)) return;
            if (selectedMinecraftVersion == "1.20.1")
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
                    .Where(version => version.StartsWith(selectedMinecraftVersion.Substring(2)))
                    .ToList());
            NeoForgeVersionComboBox.IsEnabled = true;
        }
    }
}
