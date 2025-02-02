using MCServerLauncher.WPF.Modules;
using MCServerLauncher.WPF.Modules.DownloadProvider;
using MCServerLauncher.WPF.View.Components.ResDownloadItem;
using MCServerLauncher.WPF.View.Pages;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace MCServerLauncher.WPF.View.ResDownloadProvider
{
    /// <summary>
    ///    MCSLSyncProvider.xaml 的交互逻辑
    /// </summary>
    public partial class MCSLSyncProvider : IResDownloadProvider
    {
        public string ResProviderName => LanguageManager.Localize["ResDownloadPage_ProviderName_MCSLSync"];
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

                CoreGridView.Items.Clear();
                CoreVersionStackPanel.Children.Clear();
                MinecraftVersionComboBox.SelectionChanged -= GetCoreDetail;
                MinecraftVersionComboBox.Items.Clear();

                MinecraftVersionComboBox.IsEnabled = false;
                IsEnabled = false;

                _isDataLoading = true;
                var mcslSyncCoreInfo = await new MCSLSync().GetCoreInfo();

                if (mcslSyncCoreInfo != null)
                {
                    foreach (var coreItem in mcslSyncCoreInfo.Select(result => new MCSLSyncResCoreItem
                    {
                        CoreName = result
                    }))
                        CoreGridView.Items.Add(coreItem);

                    _isDataLoading = false;
                    _isDataLoaded = true;
                    Log.Information($"[Res] [MCSL-Sync] Core info loaded. Count: {mcslSyncCoreInfo.Count}");
                }

                MinecraftVersionComboBox.SelectionChanged += GetCoreDetail;
                IsEnabled = true;

                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[Res] [MCSL-Sync] Failed to load core info. Reason: {ex.Message}");
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
        private async void SetCore(object sender, SelectionChangedEventArgs e)
        {
            if (CoreGridView.SelectedIndex == -1) return;
            var selectedCore = (MCSLSyncResCoreItem)CoreGridView.SelectedItem;
            Log.Information($"[Res] [MCSL-Sync] Selected core \"{selectedCore.CoreName}\"");
            MinecraftVersionComboBox.SelectionChanged -= GetCoreDetail;
            MinecraftVersionComboBox.Items.Clear();
            CoreGridView.IsEnabled = false;
            MinecraftVersionComboBox.IsEnabled = false;
            List<string?> minecraftVersions = DownloadManager.SequenceMinecraftVersion((await new MCSLSync().GetMinecraftVersions(selectedCore.CoreName))!);
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

                if (mcslSyncCoreVersions != null)
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