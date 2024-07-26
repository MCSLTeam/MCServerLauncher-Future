using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;
using MCServerLauncher.WPF.Main.Modules.Download;
using MCServerLauncher.WPF.Main.View.Components;
using Serilog;

namespace MCServerLauncher.WPF.Main.View.ResDownloadProvider
{
    /// <summary>
    ///     ZCloudFileProvider.xaml 的交互逻辑
    /// </summary>
    public partial class ZCloudFileProvider
    {
        public readonly string ResProviderName = "ZCloud File";
        private bool _isDataLoaded;
        private bool _isDataLoading;

        public ZCloudFileProvider()
        {
            InitializeComponent();
        }

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
                Log.Information($"[Res] [ZCloudFile] Core info loaded. Count: {zCloudFileInfo.Count}");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[Res] [ZCloudFile] Failed to load core info. Reason: {ex.Message}");
                return false;
            }
        }

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
                zCloudFileInfo.Reverse();
                CoreVersionStackPanel.Children.Clear();
                foreach (var coreDetailItem in zCloudFileInfo.Select(detail => new ZCloudFileResCoreVersionItem
                         {
                             Core = selectedCore.CoreName,
                             FileName = detail.FileName,
                             FileSize = $"{int.Parse(detail.FileSize) / 1024.00 / 1024.00:0.00} MB"
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