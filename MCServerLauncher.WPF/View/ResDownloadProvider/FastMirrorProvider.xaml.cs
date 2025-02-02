using iNKORE.UI.WPF.Helpers;
using MCServerLauncher.WPF.Modules;
using MCServerLauncher.WPF.Modules.DownloadProvider;
using MCServerLauncher.WPF.View.Components.ResDownloadItem;
using MCServerLauncher.WPF.View.Pages;
using Serilog;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using static MCServerLauncher.WPF.Modules.VisualTreeHelper;

namespace MCServerLauncher.WPF.View.ResDownloadProvider
{
    /// <summary>
    ///    FastMirrorProvider.xaml 的交互逻辑
    /// </summary>
    public partial class FastMirrorProvider : IResDownloadProvider
    {
        public string ResProviderName => LanguageManager.Localize["ResDownloadPage_ProviderName_FastMirror"];
        private bool _isDataLoaded;
        private bool _isDataLoading;

        public FastMirrorProvider()
        {
            InitializeComponent();
        }

        /// <summary>
        ///    Refresh core list.
        /// </summary>
        /// <returns>Status, true or false.</returns>
        public async Task<bool> Refresh()
        {
            if (_isDataLoading || _isDataLoaded) return true;
            try
            {
                Log.Information("[Res] [FastMirror] Loading core info");

                MinecraftVersionComboBox.SelectionChanged -= GetCoreDetail;
                CoreGridView.Items.Clear();
                MinecraftVersionComboBox.Items.Clear();
                CoreVersionStackPanel.Children.Clear();

                MinecraftVersionComboBox.IsEnabled = false;
                CoreHomePageButton.IsEnabled = false;
                IsEnabled = false;

                _isDataLoading = true;
                var fastMirrorInfo = await new FastMirror().GetCoreInfo();

                if (fastMirrorInfo != null)
                {
                    foreach (var coreItem in fastMirrorInfo.Select(result => new FastMirrorResCoreItem
                    {
                        CoreName = result.Name,
                        CoreTag = result.Tag,
                        Recommend = result.Recommend,
                        HomePage = result.HomePage,
                        MinecraftVersions = DownloadManager.SequenceMinecraftVersion(result.MinecraftVersions)
                    }))
                        CoreGridView.Items.Add(coreItem);

                    _isDataLoading = false;
                    _isDataLoaded = true;
                    Log.Information($"[Res] [FastMirror] Core info loaded. Count: {fastMirrorInfo.Count}");
                }

                MinecraftVersionComboBox.SelectionChanged += GetCoreDetail;
                IsEnabled = true;

                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[Res] [FastMirror] Failed to load core info. Reason: {ex.Message}");
                return false;
            }
            finally
            {
                _isDataLoading = false;
                _isDataLoaded = false;
                var parent = this.TryFindParent<ResDownloadPage>();
                parent?.HideLoadingLayer();
            }
        }

        /// <summary>
        ///    Handler for core selection changed, load Minecraft version list.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SetCore(object sender, SelectionChangedEventArgs e)
        {
            if (CoreGridView.SelectedIndex == -1) return;
            MinecraftVersionComboBox.SelectionChanged -= GetCoreDetail;
            var selectedCore = (FastMirrorResCoreItem)CoreGridView.SelectedItem;
            Log.Information($"[Res] [FastMirror] Selected core \"{selectedCore.CoreName}\"");
            MinecraftVersionComboBox.Items.Clear();
            CoreGridView.IsEnabled = false;
            CoreHomePageButton.IsEnabled = false;
            MinecraftVersionComboBox.IsEnabled = false;
            if (selectedCore.MinecraftVersions is not null)
                foreach (var minecraftVersion in selectedCore.MinecraftVersions)
                    MinecraftVersionComboBox.Items.Add($"Minecraft {minecraftVersion}");
            MinecraftVersionComboBox.SelectionChanged += GetCoreDetail;
            MinecraftVersionComboBox.SelectedIndex = 0;
            if (selectedCore.HomePage != null) CoreHomePageButton.SetProperty("HomePage", selectedCore.HomePage);
            CoreHomePageButton.IsEnabled = true;
            CoreGridView.IsEnabled = true;
            MinecraftVersionComboBox.IsEnabled = true;
        }

        /// <summary>
        ///    Open core home page.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OpenHomePage(object sender, RoutedEventArgs e)
        {
            Network.OpenUrl(((FastMirrorResCoreItem)CoreGridView.SelectedItem).GetProperty("HomePage").ToString());
        }

        /// <summary>
        ///    Get core version detail.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void GetCoreDetail(object sender, SelectionChangedEventArgs e)
        {
            var currentCore = (FastMirrorResCoreItem)CoreGridView.SelectedItem;
            var currentMinecraftVersion = MinecraftVersionComboBox.SelectedItem.ToString().Replace("Minecraft ", "");
            if (currentCore.CoreName == null || currentMinecraftVersion == null) return;
            CoreGridView.IsEnabled = false;
            MinecraftVersionComboBox.IsEnabled = false;
            Log.Information(
                $"[Res] [FastMirror] Selected core \"{currentCore.CoreName}\" with Minecraft version \"{currentMinecraftVersion}\"");
            try
            {
                var fastMirrorCoreDetails =
                    await new FastMirror().GetCoreDetail(currentCore.CoreName, currentMinecraftVersion);
                CoreVersionStackPanel.Children.Clear();
                if (fastMirrorCoreDetails != null)
                {
                    foreach (var coreDetailItem in fastMirrorCoreDetails.Select(detail =>
                                 new FastMirrorResCoreVersionItem
                                 {
                                     Core = detail.Name,
                                     CoreVersion = detail.CoreVersion,
                                     MinecraftVersion = detail.MinecraftVersion,
                                 }))
                        CoreVersionStackPanel.Children.Add(coreDetailItem);

                    Log.Information($"[Res] [FastMirror] Core detail loaded. Count: {fastMirrorCoreDetails.Count}");
                }
            }
            catch (Exception ex)
            {
                Log.Error(
                    $"[Res] [FastMirror] Failed to get core detail of \"{currentCore.CoreName}\" with Minecraft version \"{currentMinecraftVersion}\". Reason: {ex.Message}");
            }

            CoreGridView.IsEnabled = true;
            MinecraftVersionComboBox.IsEnabled = true;
        }
    }
}