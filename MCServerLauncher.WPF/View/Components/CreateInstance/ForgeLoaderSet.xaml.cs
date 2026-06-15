using iNKORE.UI.WPF.DragDrop.Utilities;
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
    ///    ForgeLoaderSet.xaml 的交互逻辑
    /// </summary>
    public partial class ForgeLoaderSet: ICreateInstanceStep
    {
        public ForgeLoaderSet()
        {
            InitializeComponent();
            LoaderSetStepHelper.BindSelectionStatus(this, MinecraftVersionComboBox, IsFinished1Property);
            LoaderSetStepHelper.BindSelectionStatus(this, ForgeVersionComboBox, IsFinished2Property);

            FetchMinecraftVersionsButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

        }

        private List<Forge.ForgeBuild>? CurrentForgeBuilds { get; set; }


        public static readonly DependencyProperty IsFinished1Property = DependencyProperty.Register(
            nameof(IsFinished1),
            typeof(bool),
            typeof(ForgeLoaderSet),
            new PropertyMetadata(false,
                LoaderSetStepHelper.CreateStatusVisibilityCallback<ForgeLoaderSet>(control => control.StatusShow1)));

        public static readonly DependencyProperty IsFinished2Property = DependencyProperty.Register(
            nameof(IsFinished2),
            typeof(bool),
            typeof(ForgeLoaderSet),
            new PropertyMetadata(false,
                LoaderSetStepHelper.CreateStatusVisibilityCallback<ForgeLoaderSet>(control => control.StatusShow2)));

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
                    ForgeVersionComboBox,
                    "Forge");
            }
        }

        /// <summary>
        ///    Determine whether to use the mirror endpoint.
        /// </summary>
        private static bool UseMirror()
        {
            return SettingsManager.Get?.InstanceCreation != null &&
                   SettingsManager.Get.InstanceCreation.UseMirrorForMinecraftForgeInstall;
        }

        /// <summary>
        ///    Main method to get specific version of Minecraft with Forge.
        /// </summary>
        private async void FetchMinecraftVersions(object sender, RoutedEventArgs e)
        {
            FetchMinecraftVersionsButton.IsEnabled = false;
            MinecraftVersionComboBox.IsEnabled = false;
            FetchForgeVersionButton.IsEnabled = false;
            ForgeVersionComboBox.IsEnabled = false;
            MinecraftVersionComboBox.ClearSelectedItems();
            ForgeVersionComboBox.ClearSelectedItems();
            MinecraftVersionComboBox.SelectionChanged -= PreFetchForgeVersions;
            MinecraftVersionComboBox.ItemsSource = DownloadManager.SequenceMinecraftVersion(
                await Forge.GetMinecraftVersions(UseMirror())
            );
            MinecraftVersionComboBox.SelectionChanged += PreFetchForgeVersions;
            FetchMinecraftVersionsButton.IsEnabled = true;
            MinecraftVersionComboBox.IsEnabled = true;
        }

        private void PreFetchForgeVersions(object sender, SelectionChangedEventArgs e)
        {
            FetchForgeVersionButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        }

        /// <summary>
        ///    Main method to get version of Forge with a particular Minecraft version.
        /// </summary>
        private async void FetchForgeVersions(object sender, RoutedEventArgs e)
        {
            FetchForgeVersionButton.IsEnabled = false;
            ForgeVersionComboBox.IsEnabled = false;
            MinecraftVersionComboBox.IsEnabled = false;
            ForgeVersionComboBox.ClearSelectedItems();
            var selectedMinecraftVersion = MinecraftVersionComboBox.SelectedItem?.ToString();
            if (string.IsNullOrWhiteSpace(selectedMinecraftVersion))
            {
                ForgeVersionComboBox.IsEnabled = true;
                FetchForgeVersionButton.IsEnabled = true;
                MinecraftVersionComboBox.IsEnabled = true;
                return;
            }

            CurrentForgeBuilds =
                await Forge.GetForgeVersions(selectedMinecraftVersion, UseMirror());
            if (CurrentForgeBuilds != null)
                ForgeVersionComboBox.ItemsSource = DownloadManager.SequenceMinecraftVersion(
                    LoaderSetStepHelper.NonEmptyStrings(
                        CurrentForgeBuilds.Select(forgeBuild => forgeBuild.ForgeVersion))
                );
            ForgeVersionComboBox.IsEnabled = true;
            FetchForgeVersionButton.IsEnabled = true;
            MinecraftVersionComboBox.IsEnabled = true;
        }
    }
}
