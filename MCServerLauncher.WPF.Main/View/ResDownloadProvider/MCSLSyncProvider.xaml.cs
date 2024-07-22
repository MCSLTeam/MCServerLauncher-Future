using MCServerLauncher.WPF.Main.Helpers;
using MCServerLauncher.WPF.Main.Modules.Download;
using MCServerLauncher.WPF.Main.View.Components;
using Serilog;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace MCServerLauncher.WPF.Main.View.ResDownloadProvider
{
    /// <summary>
    /// MCSLSyncProvider.xaml 的交互逻辑
    /// </summary>
    public partial class MCSLSyncProvider : UserControl
    {
        private bool IsDataLoading = false;
        private bool IsDataLoaded = false;
        public string ResProviderName = "MCSL-Sync 同步镜像";

        public MCSLSyncProvider()
        {
            InitializeComponent();
        }
        public async Task<bool> Refresh()
        {
            if (IsDataLoading || IsDataLoaded)
            {
                return true;
            }
            try
            {
                Log.Information("[Res] [MCSL-Sync] Loading core info");
                IsDataLoading = true;
                List<string> MCSLSyncCoreInfo = await new MCSLSync().GetCoreInfo();

                foreach (string Result in MCSLSyncCoreInfo)
                {
                    MCSLSyncResCoreItem CoreItem = new()
                    {
                        CoreName = Result
                    };
                    CoreGridView.Items.Add(CoreItem);
                }

                IsDataLoading = false;
                IsDataLoaded = true;
                Log.Information($"[Res] [MCSL-Sync] Core info loaded. Count: {MCSLSyncCoreInfo.Count}");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[Res] [MCSL-Sync] Failed to load core info. Reason: {ex.Message}");
                return false;
            }
        }
        private async void SetCore(object sender, SelectionChangedEventArgs e)
        {
            if (CoreGridView.SelectedIndex == -1)
            {
                return;
            }
            MCSLSyncResCoreItem SelectedCore = (MCSLSyncResCoreItem)CoreGridView.SelectedItem;
            Log.Information($"[Res] [MCSL-Sync] Selected core \"{SelectedCore.CoreName}\"");
            MinecraftVersionComboBox.SelectionChanged -= new SelectionChangedEventHandler(GetCoreDetail);
            MinecraftVersionComboBox.Items.Clear();
            CoreGridView.IsEnabled = false;
            MinecraftVersionComboBox.IsEnabled = false;
            List<string> MinecraftVersions = await new MCSLSync().GetMinecraftVersions(SelectedCore.CoreName);
            MinecraftVersions = ResDownloadUtils.SequenceMinecraftVersion(MinecraftVersions);
            foreach (string MinecraftVersion in MinecraftVersions)
            {
                MinecraftVersionComboBox.Items.Add($"Minecraft {MinecraftVersion}");
            }
            MinecraftVersionComboBox.SelectionChanged += new SelectionChangedEventHandler(GetCoreDetail);
            MinecraftVersionComboBox.SelectedIndex = 0;
            CoreGridView.IsEnabled = true;
            MinecraftVersionComboBox.IsEnabled = true;

        }

        private async void GetCoreDetail(object sender, SelectionChangedEventArgs e)
        {
            MCSLSyncResCoreItem CurrentCore = (MCSLSyncResCoreItem)CoreGridView.SelectedItem;
            string CurrentMinecraftVersion = MinecraftVersionComboBox.SelectedItem.ToString().Replace("Minecraft ", "");
            if (CurrentCore.CoreName == null || CurrentMinecraftVersion == null)
            {
                return;
            }
            CoreGridView.IsEnabled = false;
            MinecraftVersionComboBox.IsEnabled = false;
            Log.Information($"[Res] [MCSL-Sync] Selected core \"{CurrentCore.CoreName}\" with Minecraft version \"{CurrentMinecraftVersion}\"");
            try
            {
                List<string> MCSLSyncCoreVersions = await new MCSLSync().GetCoreVersions(CurrentCore.CoreName, CurrentMinecraftVersion);
                CoreVersionStackPanel.Children.Clear();
                foreach (string Detail in MCSLSyncCoreVersions)
                {
                    MCSLSyncResCoreVersionItem CoreDetailItem = new()
                    {
                        Core = CurrentCore.CoreName,
                        CoreVersion = Detail,
                        MinecraftVersion = CurrentMinecraftVersion
                    };
                    CoreVersionStackPanel.Children.Add(CoreDetailItem);
                }
                Log.Information($"[Res] [MCSL-Sync] Core list loaded. Count: {MCSLSyncCoreVersions.Count}");
            }
            catch (Exception ex)
            {
                Log.Error($"[Res] [MCSL-Sync] Failed to get core list of \"{CurrentCore.CoreName}\" with Minecraft version \"{CurrentMinecraftVersion}\". Reason: {ex.Message}");
            }
            CoreGridView.IsEnabled = true;
            MinecraftVersionComboBox.IsEnabled = true;
        }

    }
}
