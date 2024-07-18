using MCServerLauncher.WPF.Modules.Download;
using System.Collections.Generic;
using System.Windows.Controls;
using static MCServerLauncher.WPF.Modules.Download.FastMirror;
using MCServerLauncher.WPF.View.Components;
using System;
using System.Threading.Tasks;
using MCServerLauncher.WPF.Helpers;
using iNKORE.UI.WPF.Helpers;
using System.Windows;
using Serilog;

namespace MCServerLauncher.WPF.View.ResDownloadProvider
{
    /// <summary>
    /// FastMirrorProvider.xaml 的交互逻辑
    /// </summary>
    public partial class FastMirrorProvider : UserControl
    {
        private bool IsDataLoading = false;
        private bool IsDataLoaded = false;
        public string ResProviderName = "无极镜像";

        public FastMirrorProvider()
        {
            InitializeComponent();
        }
        public async Task<bool> Refresh(Func<List<string>, List<string>> SequenceFunc)
        {
            if (IsDataLoading || IsDataLoaded)
            {
                return true;
            }
            try
            {
                Log.Information("[Res] [FastMirror] Loading core info");
                IsDataLoading = true;
                List<FastMirrorCoreInfo> FastMirrorInfo = await new FastMirror().GetCoreInfo();

                foreach (FastMirrorCoreInfo Result in FastMirrorInfo)
                {
                    FastMirrorResCoreItem CoreItem = new()
                    {
                        CoreName = Result.Name,
                        CoreTag = Result.Tag,
                        Recommend = Result.Recommend,
                        HomePage = Result.HomePage,
                        MinecraftVersions = SequenceFunc(Result.MinecraftVersions)
                    };
                    CoreGridView.Items.Add(CoreItem);
                }

                IsDataLoading = false;
                IsDataLoaded = true;
                Log.Information($"[Res] [FastMirror] Core info loaded. Count: {FastMirrorInfo.Count}");
                return true;
            } catch (Exception ex)
            {
                Log.Error($"[Res] [FastMirror] Failed to load core info. Reason: {ex.Message}");
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
            Log.Information($"[Res] [FastMirror] Selected core \"{SelectedCore.CoreName}\"");
            MinecraftVersionComboBox.SelectionChanged -= new SelectionChangedEventHandler(GetCoreDetail);
            MinecraftVersionComboBox.Items.Clear();
            CoreGridView.IsEnabled = false;
            CoreHomePageButton.IsEnabled = false;
            MinecraftVersionComboBox.IsEnabled = false;
            foreach (string MinecraftVersion in SelectedCore.MinecraftVersions)
            {
                MinecraftVersionComboBox.Items.Add($"Minecraft {MinecraftVersion}");
            }
            MinecraftVersionComboBox.SelectionChanged += new SelectionChangedEventHandler(GetCoreDetail);
            MinecraftVersionComboBox.SelectedIndex = 0;
            CoreHomePageButton.SetProperty("HomePage", SelectedCore.HomePage);
            CoreHomePageButton.IsEnabled = true;
            CoreGridView.IsEnabled = true;
            MinecraftVersionComboBox.IsEnabled = true;

        }
        private void OpenHomePage(object Sender, RoutedEventArgs e)
        {
            NetworkUtils.OpenUrl(((FastMirrorResCoreItem)CoreGridView.SelectedItem).GetProperty("HomePage").ToString());
        }

        private async void GetCoreDetail(object sender, SelectionChangedEventArgs e)
        {
            FastMirrorResCoreItem CurrentCore = (FastMirrorResCoreItem)CoreGridView.SelectedItem;
            string CurrentMinecraftVersion = MinecraftVersionComboBox.SelectedItem.ToString().Replace("Minecraft ","");
            if (CurrentCore.CoreName == null || CurrentMinecraftVersion == null)
            {
                return;
            }
            CoreGridView.IsEnabled = false;
            MinecraftVersionComboBox.IsEnabled = false;
            Log.Information($"[Res] [FastMirror] Selected core \"{CurrentCore.CoreName}\" with Minecraft version \"{CurrentMinecraftVersion}\"");
            try
            {
                List<FastMirrorCoreDetail> FastMirrorCoreDetails = await new FastMirror().GetCoreDetail(CurrentCore.CoreName, CurrentMinecraftVersion);
                CoreVersionStackPanel.Children.Clear();
                foreach (FastMirrorCoreDetail Detail in FastMirrorCoreDetails)
                {
                    FastMirrorResCoreVersionItem CoreDetailItem = new()
                    {
                        Core = Detail.Name,
                        CoreVersion = Detail.CoreVersion,
                        MinecraftVersion = Detail.MinecraftVersion
                    };
                    CoreVersionStackPanel.Children.Add(CoreDetailItem);
                }
                Log.Information($"[Res] [FastMirror] Core detail loaded. Count: {FastMirrorCoreDetails.Count}");
            } catch (Exception ex)
            {
                Log.Error($"[Res] [FastMirror] Failed to get core detail of \"{CurrentCore.CoreName}\" with Minecraft version \"{CurrentMinecraftVersion}\". Reason: {ex.Message}");
            }
            CoreGridView.IsEnabled = true;
            MinecraftVersionComboBox.IsEnabled = true;
        }

    }
}
