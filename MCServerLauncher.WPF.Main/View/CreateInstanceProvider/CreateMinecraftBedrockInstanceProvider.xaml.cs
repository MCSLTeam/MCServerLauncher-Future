﻿using System.Windows;
using static MCServerLauncher.WPF.Main.Helpers.VisualTreeExtensions;

namespace MCServerLauncher.WPF.Main.View.CreateInstanceProvider
{
    /// <summary>
    ///     CreateMinecraftBedrockInstanceProvider.xaml 的交互逻辑
    /// </summary>
    public partial class CreateMinecraftBedrockInstanceProvider
    {
        public CreateMinecraftBedrockInstanceProvider()
        {
            InitializeComponent();
        }

        private void GoPreCreateInstance(object sender, RoutedEventArgs e)
        {
            var parent = this.TryFindParent<CreateInstancePage>();
            parent.CurrentCreateInstance.GoBack();
        }


        private void FinishSetup(object sender, RoutedEventArgs e)
        {
        }
    }
}