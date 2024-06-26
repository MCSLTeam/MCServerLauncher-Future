﻿using MCServerLauncher.UI.View.Components;
using System.Windows;
using System.Windows.Controls;
using static MCServerLauncher.UI.Helpers.VisualTreeExtensions;

namespace MCServerLauncher.UI.View.CreateInstanceProvider
{
    /// <summary>
    /// CreateMinecraftForgeInstanceProvider.xaml 的交互逻辑
    /// </summary>
    public partial class CreateMinecraftForgeInstanceProvider : UserControl
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
            return;
        }
        private void AddJVMArgument(object sender, RoutedEventArgs e)
        {
            JVMArgumentListView.Items.Add(new JVMArgumentItem());
        }
    }
}
