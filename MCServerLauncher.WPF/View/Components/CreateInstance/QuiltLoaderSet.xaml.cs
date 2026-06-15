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
    ///    QuiltLoaderSet.xaml 的交互逻辑
    /// </summary>
    public partial class QuiltLoaderSet : ICreateInstanceStep
    {
        public QuiltLoaderSet()
        {
            InitializeComponent();
            LoaderSetStepHelper.BindSelectionStatus(this, MinecraftVersionComboBox, IsFinished1Property);
            LoaderSetStepHelper.BindSelectionStatus(this, QuiltVersionComboBox, IsFinished2Property);

            ToggleStableMinecraftVersionCheckBox.Checked += ToggleStableMinecraftVersion;
            ToggleStableMinecraftVersionCheckBox.Unchecked += ToggleStableMinecraftVersion;

            FetchMinecraftVersionsButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            FetchQuiltVersionButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        }

        private List<Quilt.QuiltMinecraftVersion>? SupportedAllMinecraftVersions { get; set; }
        private List<string>? QuiltLoaderVersions { get; set; }

        public static readonly DependencyProperty IsFinished1Property = DependencyProperty.Register(
            nameof(IsFinished1),
            typeof(bool),
            typeof(QuiltLoaderSet),
            new PropertyMetadata(false,
                LoaderSetStepHelper.CreateStatusVisibilityCallback<QuiltLoaderSet>(control => control.StatusShow1)));

        public static readonly DependencyProperty IsFinished2Property = DependencyProperty.Register(
            nameof(IsFinished2),
            typeof(bool),
            typeof(QuiltLoaderSet),
            new PropertyMetadata(false,
                LoaderSetStepHelper.CreateStatusVisibilityCallback<QuiltLoaderSet>(control => control.StatusShow2)));

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
                    QuiltVersionComboBox,
                    "Quilt");
            }
        }

        /// <summary>
        ///    Determine whether to use the mirror endpoint.
        /// </summary>
        private static bool UseMirror()
        {
            return SettingsManager.Get?.InstanceCreation != null &&
                   SettingsManager.Get.InstanceCreation.UseMirrorForMinecraftQuiltInstall;
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
            SupportedAllMinecraftVersions = await Quilt.GetMinecraftVersions(UseMirror());
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
                    LoaderSetStepHelper.NonEmptyStrings(ToggleStableMinecraftVersionCheckBox.IsChecked.GetValueOrDefault(true)
                        ? SupportedAllMinecraftVersions.Where(mcVersion => mcVersion.IsStable).ToList()
                            .Select(mcVersion => mcVersion.MinecraftVersion)
                        : SupportedAllMinecraftVersions.Select(mcVersion => mcVersion.MinecraftVersion))
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
            QuiltLoaderVersions = await Quilt.GetQuiltVersions(UseMirror());
            QuiltVersionComboBox.ItemsSource = QuiltLoaderVersions;
            QuiltVersionComboBox.IsEnabled = true;
            FetchQuiltVersionButton.IsEnabled = true;
        }
    }
}
