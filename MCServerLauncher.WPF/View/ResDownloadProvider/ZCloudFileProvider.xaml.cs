using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;
using MCServerLauncher.WPF.Modules.DownloadProvider;
using MCServerLauncher.WPF.View.Components.ResDownloadItem;
using Serilog;

namespace MCServerLauncher.WPF.View.ResDownloadProvider
{
    /// <summary>
    ///    ZCloudFileProvider.xaml 的交互逻辑
    /// </summary>
    public partial class ZCloudFileProvider : IResDownloadProvider
    {
        public string ResProviderName => "ZCloud File";
        private bool _isDataLoaded;
        private bool _isDataLoading;

        public ZCloudFileProvider()
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
                Log.Information("[Res] [ZCloudFile] Loading core info");
                _isDataLoading = true;
                var zCloudFileInfo = await new AList().GetFileList("https://jn.sv.ztsin.cn:5244", "MCSL2/MCSLAPI");

                foreach (var coreItem in zCloudFileInfo.Select(result => new ZCloudFileResCoreItem
                {
                    CoreName = result.FileName
                }))
                    CoreGridView.Items.Add(coreItem);

                _isDataLoading = false;
                _isDataLoaded = true;
                if (zCloudFileInfo != null)
                    Log.Information($"[Res] [ZCloudFile] Core info loaded. Count: {zCloudFileInfo.Count}");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[Res] [ZCloudFile] Failed to load core info. Reason: {ex.Message}");
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
            var selectedCore = (ZCloudFileResCoreItem)CoreGridView.SelectedItem;
            Log.Information($"[Res] [ZCloudFile] Selected core \"{selectedCore.CoreName}\"");
            CurrentCoreName.Text = selectedCore.CoreName;
            CoreGridView.IsEnabled = false;
            try
            {
                var zCloudFileInfo = await new AList().GetFileList("https://jn.sv.ztsin.cn:5244",
                    $"MCSL2/MCSLAPI/{selectedCore.CoreName}");
                zCloudFileInfo?.Reverse();
                CoreVersionStackPanel.Children.Clear();
                foreach (var coreDetailItem in (zCloudFileInfo ?? throw new InvalidOperationException()).Select(detail => new ZCloudFileResCoreVersionItem
                {
                    Core = selectedCore.CoreName,
                    FileName = detail.FileName,
                    FileSize = $"{int.Parse(detail.FileSize ?? throw new InvalidOperationException()) / 1024.00 / 1024.00:0.00} MB"
                }))
                    CoreVersionStackPanel.Children.Add(coreDetailItem);

                Log.Information($"[Res] [ZCloudFile] Core list loaded. Count: {zCloudFileInfo.Count}");
            }
            catch (Exception ex)
            {
                Log.Error(
                    $"[Res] [ZCloudFile] Failed to get core list of \"{selectedCore.CoreName}\". Reason: {ex.Message}");
            }

            CoreGridView.IsEnabled = true;
        }
    }
}