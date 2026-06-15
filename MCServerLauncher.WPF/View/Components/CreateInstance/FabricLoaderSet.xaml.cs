using iNKORE.UI.WPF.DragDrop.Utilities;
using MCServerLauncher.Common.Minecraft.InstallSource;
using MCServerLauncher.WPF.Modules;
using System;
using System.Collections.Generic;
using System.Linq;
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
            VisualTreeHelper.InitStepState(MinecraftVersionComboBox);
            VisualTreeHelper.InitStepState(FabricVersionComboBox);

            ToggleStableMinecraftVersionCheckBox.Checked += ToggleStableMinecraftVersion;
            ToggleStableMinecraftVersionCheckBox.Unchecked += ToggleStableMinecraftVersion;

            ToggleStableFabricVersionCheckBox.Checked += ToggleStableFabricVersion;
            ToggleStableFabricVersionCheckBox.Unchecked += ToggleStableFabricVersion;

            FetchMinecraftVersionsButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        }
#nullable enable
        private List<Fabric.FabricUniversalVersion>? SupportedAllMinecraftVersions { get; set; }
        private List<Fabric.FabricUniversalVersion>? SupportedAllFabricVersions { get; set; }

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

        public CreateInstanceData ActualData
        {
            get
            {
                var mcVersion = MinecraftVersionComboBox.SelectedItem?.ToString();
                var loaderVersion = FabricVersionComboBox.SelectedItem?.ToString();
                if (string.IsNullOrWhiteSpace(mcVersion) || string.IsNullOrWhiteSpace(loaderVersion))
                    throw new InvalidOperationException("Minecraft and Fabric versions must be selected.");

                return new CreateInstanceData
                {
                    Type = CreateInstanceDataType.Struct,
                    Data = new MinecraftLoaderVersion
                    {
                        MCVersion = mcVersion,
                        LoaderVersion = loaderVersion,
                    }
                };
            }
        }

        /// <summary>
        ///    Determine whether to use the mirror endpoint.
        /// </summary>
        private static bool UseMirror()
        {
            return SettingsManager.Get?.InstanceCreation != null &&
                   SettingsManager.Get.InstanceCreation.UseMirrorForMinecraftFabricInstall;
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
            SupportedAllMinecraftVersions = await Fabric.GetMinecraftVersions(UseMirror());
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
            var versions = SupportedAllMinecraftVersions ?? [];
            MinecraftVersionComboBox.ItemsSource = ToggleStableMinecraftVersionCheckBox.IsChecked.GetValueOrDefault(true)
                    ? versions.Where(mcVersion => mcVersion.IsStable).Select(mcVersion => mcVersion.Version).ToList()
                    : versions.Select(mcVersion => mcVersion.Version).ToList();
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
            SupportedAllFabricVersions = await Fabric.GetFabricVersions(UseMirror());
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
            var versions = SupportedAllFabricVersions ?? [];
            FabricVersionComboBox.ItemsSource = ToggleStableFabricVersionCheckBox.IsChecked.GetValueOrDefault(true)
                ? versions.Where(fabricVersion => fabricVersion.IsStable).Select(mcVersion => mcVersion.Version).ToList()
                : versions.Select(fabricVersion => fabricVersion.Version).ToList();
            FabricVersionComboBox.IsEnabled = true;
            ToggleStableFabricVersionCheckBox.IsEnabled = true;
        }
#nullable disable
    }
}
