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

        public CreateInstanceData ActualData
        {
            get
            {
                var mcVersion = MinecraftVersionComboBox.SelectedItem?.ToString();
                var loaderVersion = NeoForgeVersionComboBox.SelectedItem?.ToString();
                if (string.IsNullOrWhiteSpace(mcVersion) || string.IsNullOrWhiteSpace(loaderVersion))
                    throw new InvalidOperationException("Minecraft and NeoForge versions must be selected.");

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
#nullable disable
    }
}
