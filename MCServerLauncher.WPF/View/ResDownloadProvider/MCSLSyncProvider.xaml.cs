using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;
using MCServerLauncher.WPF.Helpers;
using MCServerLauncher.WPF.Modules.Download;
using MCServerLauncher.WPF.View.Components.ResDownloadItem;
using Serilog;

namespace MCServerLauncher.WPF.View.ResDownloadProvider
{
    /// <summary>
    ///    MCSLSyncProvider.xaml 的交互逻辑
    /// </summary>
    public partial class MCSLSyncProvider
    {
        public readonly string ResProviderName = "MCSL-Sync 同步镜像";
        private bool _isDataLoaded;
        private bool _isDataLoading;

        public MCSLSyncProvider()
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
                Log.Information("[Res] [MCSL-Sync] Loading core info");
                _isDataLoading = true;
                var mcslSyncCoreInfo = await new MCSLSync().GetCoreInfo();

                foreach (var coreItem in mcslSyncCoreInfo.Select(result => new MCSLSyncResCoreItem
                         {
                             CoreName = result
                         }))
                    CoreGridView.Items.Add(coreItem);

                _isDataLoading = false;
                _isDataLoaded = true;
                Log.Information($"[Res] [MCSL-Sync] Core info loaded. Count: {mcslSyncCoreInfo.Count}");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[Res] [MCSL-Sync] Failed to load core info. Reason: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        ///    Handler for core selection changed, load Minecraft version list.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void SetCore(object sender, SelectionChangedEventArgs e)
        {
            if (CoreGridView.SelectedIndex == -1) return;
            var selectedCore = (MCSLSyncResCoreItem)CoreGridView.SelectedItem;
            Log.Information($"[Res] [MCSL-Sync] Selected core \"{selectedCore.CoreName}\"");
            MinecraftVersionComboBox.SelectionChanged -= GetCoreDetail;
            MinecraftVersionComboBox.Items.Clear();
            CoreGridView.IsEnabled = false;
            MinecraftVersionComboBox.IsEnabled = false;
            var minecraftVersions = await new MCSLSync().GetMinecraftVersions(selectedCore.CoreName);
            minecraftVersions = ResDownloadUtils.SequenceMinecraftVersion(minecraftVersions);
            foreach (var minecraftVersion in minecraftVersions)
                MinecraftVersionComboBox.Items.Add($"Minecraft {minecraftVersion}");
            MinecraftVersionComboBox.SelectionChanged += GetCoreDetail;
            MinecraftVersionComboBox.SelectedIndex = 0;
            CoreGridView.IsEnabled = true;
            MinecraftVersionComboBox.IsEnabled = true;
        }

        /// <summary>
        ///    Get core version detail.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void GetCoreDetail(object sender, SelectionChangedEventArgs e)
        {
            var currentCore = (MCSLSyncResCoreItem)CoreGridView.SelectedItem;
            var currentMinecraftVersion = MinecraftVersionComboBox.SelectedItem.ToString().Replace("Minecraft ", "");
            if (currentCore.CoreName == null || currentMinecraftVersion == null) return;
            CoreGridView.IsEnabled = false;
            MinecraftVersionComboBox.IsEnabled = false;
            Log.Information(
                $"[Res] [MCSL-Sync] Selected core \"{currentCore.CoreName}\" with Minecraft version \"{currentMinecraftVersion}\"");
            try
            {
                var mcslSyncCoreVersions =
                    await new MCSLSync().GetCoreVersions(currentCore.CoreName, currentMinecraftVersion);
                CoreVersionStackPanel.Children.Clear();
                foreach (var coreDetailItem in mcslSyncCoreVersions.Select(detail => new MCSLSyncResCoreVersionItem
                         {
                             Core = currentCore.CoreName,
                             CoreVersion = detail,
                             MinecraftVersion = currentMinecraftVersion
                         }))
                    CoreVersionStackPanel.Children.Add(coreDetailItem);

                Log.Information($"[Res] [MCSL-Sync] Core list loaded. Count: {mcslSyncCoreVersions.Count}");
            }
            catch (Exception ex)
            {
                Log.Error(
                    $"[Res] [MCSL-Sync] Failed to get core list of \"{currentCore.CoreName}\" with Minecraft version \"{currentMinecraftVersion}\". Reason: {ex.Message}");
            }

            CoreGridView.IsEnabled = true;
            MinecraftVersionComboBox.IsEnabled = true;
        }
    }
}