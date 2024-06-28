using MCServerLauncher.UI.Modules.Download;
using System.Collections.Generic;
using System.Windows.Controls;
using static MCServerLauncher.UI.Modules.Download.FastMirror;
using MCServerLauncher.UI.View.Components;
using System;
using System.Threading.Tasks;
using System.Windows.Input;
using MCServerLauncher.UI.Helpers;
using iNKORE.UI.WPF.Helpers;
using System.Windows;
using MCServerLauncher.UI.View.Components;
using System;
using System.Threading.Tasks;
using System.Windows.Input;
using MCServerLauncher.UI.Helpers;
using iNKORE.UI.WPF.Helpers;
using System.Windows;

namespace MCServerLauncher.UI.View.ResDownloadProvider
{
    /// <summary>
    /// FastMirrorProvider.xaml 的交互逻辑
    /// </summary>
    public partial class FastMirrorProvider : UserControl
    {
        private bool IsLoading = false;
        private bool IsLoaded = false;

        public FastMirrorProvider()
        {
            InitializeComponent();
        }
        public async Task<bool> Refresh()
        {
            if (IsLoading || IsLoaded)
            {
                return true;
            }
            try
            {
                IsLoading = true;
                List<FastMirrorCoreInfo> FastMirrorInfo = await new FastMirror().GetCoreInfo();
                foreach (FastMirrorCoreInfo Result in FastMirrorInfo)
                {
                    FastMirrorResCoreItem CoreItem = new();
                    CoreItem.CoreName = Result.Name;
                    CoreItem.CoreTag = Result.Tag;
                    CoreItem.Recommend = Result.Recommend;
                    CoreItem.HomePage = Result.HomePage;
                    CoreItem.MinecraftVersions = Result.MinecraftVersions;
                    CoreGridView.Items.Add(CoreItem);
                }
                IsLoading = false;
                IsLoaded = true;
                return true;
            } catch (Exception)
            {
                return false;
            }
        }
        private void SetCore(object sender, SelectionChangedEventArgs e)
        {
            if (CoreGridView.SelectedIndex == -1)
            {
                return;
            }
            var SelectedCore = (FastMirrorResCoreItem)CoreGridView.SelectedItem;
            MinecraftVersionComboBox.Items.Clear();
            foreach (string MinecraftVersion in SelectedCore.MinecraftVersions)
            {
                MinecraftVersionComboBox.Items.Add($"Minecraft {MinecraftVersion}");
            }
            CoreHomePageButton.SetProperty("HomePage", SelectedCore.HomePage);
            CoreHomePageButton.IsEnabled = true;

        }
        private async void OpenHomePage(object Sender, RoutedEventArgs e)
        {
            await new BasicUtils().OpenUrl((string)(Sender.GetProperty("HomePage")));
        }


        private async void GetCoreDetail(object sender, SelectionChangedEventArgs e)
        {
            string CurrentCore = ((FastMirrorResCoreItem)CoreGridView.SelectedItem).CoreName;
            string CurrentMinecraftVersion = MinecraftVersionComboBox.SelectedItem.ToString().Replace("Minecraft ","");
            if (CurrentCore == null || CurrentMinecraftVersion == null)
            {
                return;
            }
            List<FastMirrorCoreDetail> FastMirrorCoreDetails = await new FastMirror().GetCoreDetail(CurrentCore, CurrentMinecraftVersion);
            CoreVersionStackPanel.Children.Clear();
            foreach (FastMirrorCoreDetail Detail in FastMirrorCoreDetails)
            {
                FastMirrorResCoreVersionItem CoreDetailItem = new();
                CoreDetailItem.Core = Detail.Name;
                CoreDetailItem.CoreVersion = Detail.CoreVersion;
                CoreDetailItem.MinecraftVersion = Detail.MinecraftVersion;
                CoreVersionStackPanel.Children.Add(CoreDetailItem);
            }
        }

    }
}
