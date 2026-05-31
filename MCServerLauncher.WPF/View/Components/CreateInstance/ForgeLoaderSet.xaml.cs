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
                    SetValue(IsFinished2Property, !(ForgeVersionComboBox.SelectedIndex == -1));
                }
            }

            MinecraftVersionComboBox.SelectionChanged += initialHandler1;
            ForgeVersionComboBox.SelectionChanged += initialHandler2;

            // As you can see, we have to trigger it manually
            VisualTreeHelper.InitStepState(MinecraftVersionComboBox);
            VisualTreeHelper.InitStepState(ForgeVersionComboBox);

            FetchMinecraftVersionsButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

        }
#nullable enable
        private List<Forge.ForgeBuild>? CurrentForgeBuilds { get; set; }


        private bool IsDisposed1 { get; set; } = false;
        private bool IsDisposed2 { get; set; } = false;

        ~ForgeLoaderSet()
        {
            IsDisposed1 = true;
            IsDisposed2 = true;
        }

        public static readonly DependencyProperty IsFinished1Property = DependencyProperty.Register(
            nameof(IsFinished1),
            typeof(bool),
            typeof(ForgeLoaderSet),
            new PropertyMetadata(false, OnStatus1Changed));

        private static void OnStatus1Changed(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not ForgeLoaderSet control) return;
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
            typeof(ForgeLoaderSet),
            new PropertyMetadata(false, OnStatus2Changed));

        private static void OnStatus2Changed(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not ForgeLoaderSet control) return;
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
                LoaderVersion = ForgeVersionComboBox.SelectedItem!.ToString(),
            }
        };

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
                await new Forge().GetMinecraftVersions(UseMirror())
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
            CurrentForgeBuilds =
                await new Forge().GetForgeVersions(MinecraftVersionComboBox.SelectedItem.ToString(), UseMirror());
            if (CurrentForgeBuilds != null)
                ForgeVersionComboBox.ItemsSource = DownloadManager.SequenceMinecraftVersion(
                    CurrentForgeBuilds.Select(forgeBuild => forgeBuild.ForgeVersion).ToList()!
                );
            ForgeVersionComboBox.IsEnabled = true;
            FetchForgeVersionButton.IsEnabled = true;
            MinecraftVersionComboBox.IsEnabled = true;
        }
#nullable disable
    }
}