﻿using System.Windows;
using MCServerLauncher.WPF.View.Components;
using static MCServerLauncher.WPF.Helpers.VisualTreeExtensions;

namespace MCServerLauncher.WPF.View.CreateInstanceProvider
{
    /// <summary>
    ///     CreateMinecraftForgeInstanceProvider.xaml 的交互逻辑
    /// </summary>
    public partial class CreateMinecraftForgeInstanceProvider
    {
        public CreateMinecraftForgeInstanceProvider()
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

        private void AddJvmArgument(object sender, RoutedEventArgs e)
        {
            JVMArgumentListView.Items.Add(new JVMArgumentItem());
        }
    }
}