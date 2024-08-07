﻿using System.Windows;
using System.Windows.Controls;
using iNKORE.UI.WPF.Modern.Media.Animation;
using static MCServerLauncher.WPF.Helpers.VisualTreeExtensions;

namespace MCServerLauncher.WPF.View.CreateInstanceProvider
{
    /// <summary>
    ///     PreCreateInstance.xaml 的交互逻辑
    /// </summary>
    public partial class PreCreateInstance
    {
        private string _creatingInstanceType = "PreCreating";

        public PreCreateInstance()
        {
            InitializeComponent();
        }

        private void GoCreateNewInstance(object sender, RoutedEventArgs e)
        {
            _creatingInstanceType = ((Button)sender).Name;
            if (_creatingInstanceType == "PreCreating") return;

            var parent = this.TryFindParent<CreateInstancePage>();
            switch (_creatingInstanceType)
            {
                case "MinecraftJavaServer":
                    parent.CurrentCreateInstance.Navigate(parent.NewMinecraftJavaServerPage(),
                        new DrillInNavigationTransitionInfo());
                    break;
                case "MinecraftForgeServer":
                    parent.CurrentCreateInstance.Navigate(parent.NewMinecraftForgeServerPage(),
                        new DrillInNavigationTransitionInfo());
                    break;
                case "MinecraftBedrockServer":
                    parent.CurrentCreateInstance.Navigate(parent.NewMinecraftBedrockServerPage(),
                        new DrillInNavigationTransitionInfo());
                    break;
                case "OtherExecutable":
                    parent.CurrentCreateInstance.Navigate(parent.NewOtherExecutablePage(),
                        new DrillInNavigationTransitionInfo());
                    break;
            }
        }
    }
}