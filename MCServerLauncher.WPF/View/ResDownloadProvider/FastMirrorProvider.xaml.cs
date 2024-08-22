using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using iNKORE.UI.WPF.Helpers;
using MCServerLauncher.WPF.Helpers;
using MCServerLauncher.WPF.Modules.Download;
using MCServerLauncher.WPF.View.Components.ResDownloadItem;
using Serilog;

namespace MCServerLauncher.WPF.View.ResDownloadProvider
{
    /// <summary>
    ///     FastMirrorProvider.xaml 的交互逻辑
    /// </summary>
    public partial class FastMirrorProvider
    {
        public readonly string ResProviderName = "无极镜像";
        private bool _isDataLoaded;
        private bool _isDataLoading;

        public FastMirrorProvider()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Refresh core list.
        /// </summary>
        /// <returns>Status, true or false.</returns>
        public async Task<bool> Refresh()
        {
            if (_isDataLoading || _isDataLoaded) return true;
            try
            {
                Log.Information("[Res] [FastMirror] Loading core info");
                _isDataLoading = true;
                var fastMirrorInfo = await new FastMirror().GetCoreInfo();

                foreach (var coreItem in fastMirrorInfo.Select(result => new FastMirrorResCoreItem
                         {
                             CoreName = result.Name,
                             CoreTag = result.Tag,
                             Recommend = result.Recommend,
                             HomePage = result.HomePage,
                             MinecraftVersions = ResDownloadUtils.SequenceMinecraftVersion(result.MinecraftVersions)
                         }))
                    CoreGridView.Items.Add(coreItem);

                _isDataLoading = false;
                _isDataLoaded = true;
                Log.Information($"[Res] [FastMirror] Core info loaded. Count: {fastMirrorInfo.Count}");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[Res] [FastMirror] Failed to load core info. Reason: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Handler for core selection changed, load Minecraft version list.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SetCore(object sender, SelectionChangedEventArgs e)
        {
            if (CoreGridView.SelectedIndex == -1) return;
            var selectedCore = (FastMirrorResCoreItem)CoreGridView.SelectedItem;
            Log.Information($"[Res] [FastMirror] Selected core \"{selectedCore.CoreName}\"");
            MinecraftVersionComboBox.SelectionChanged -= GetCoreDetail;
            MinecraftVersionComboBox.Items.Clear();
            CoreGridView.IsEnabled = false;
            CoreHomePageButton.IsEnabled = false;
            MinecraftVersionComboBox.IsEnabled = false;
            foreach (var minecraftVersion in selectedCore.MinecraftVersions)
                MinecraftVersionComboBox.Items.Add($"Minecraft {minecraftVersion}");
            MinecraftVersionComboBox.SelectionChanged += GetCoreDetail;
            MinecraftVersionComboBox.SelectedIndex = 0;
            CoreHomePageButton.SetProperty("HomePage", selectedCore.HomePage);
            CoreHomePageButton.IsEnabled = true;
            CoreGridView.IsEnabled = true;
            MinecraftVersionComboBox.IsEnabled = true;
        }

        /// <summary>
        /// Open core home page.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OpenHomePage(object sender, RoutedEventArgs e)
        {
            NetworkUtils.OpenUrl(((FastMirrorResCoreItem)CoreGridView.SelectedItem).GetProperty("HomePage").ToString());
        }

        /// <summary>
        /// Get core version detail.
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
                foreach (var coreDetailItem in fastMirrorCoreDetails.Select(detail => new FastMirrorResCoreVersionItem
                         {
                             Core = detail.Name,
                             CoreVersion = detail.CoreVersion,
                             MinecraftVersion = detail.MinecraftVersion
                         }))
                    CoreVersionStackPanel.Children.Add(coreDetailItem);

                Log.Information($"[Res] [FastMirror] Core detail loaded. Count: {fastMirrorCoreDetails.Count}");
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