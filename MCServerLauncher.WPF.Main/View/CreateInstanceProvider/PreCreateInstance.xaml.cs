﻿using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using iNKORE.UI.WPF.Modern.Media.Animation;
using static MCServerLauncher.WPF.Main.Helpers.VisualTreeExtensions;

namespace MCServerLauncher.WPF.Main.View.CreateInstanceProvider
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

        private void SelectNewInstanceType(object sender, MouseButtonEventArgs mouseArg)
        {
            Console.WriteLine(((Border)sender).Name);
            _creatingInstanceType = ((Border)sender).Name;
            Console.WriteLine(_creatingInstanceType);
            SelectNewInstanceTypeContinueBtn.IsEnabled = true;
        }

        private void GoCreateInstance(object sender, RoutedEventArgs e)
        {
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