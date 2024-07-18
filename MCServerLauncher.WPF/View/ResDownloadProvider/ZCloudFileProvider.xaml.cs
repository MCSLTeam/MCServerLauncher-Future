using MCServerLauncher.WPF.Modules.Download;
using MCServerLauncher.WPF.View.Components;
using Serilog;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Controls;
using static MCServerLauncher.WPF.Modules.Download.AList;

namespace MCServerLauncher.WPF.View.ResDownloadProvider
{
    /// <summary>
    /// ZCloudFileProvider.xaml 的交互逻辑
    /// </summary>
    public partial class ZCloudFileProvider : UserControl
    {
        private bool IsDataLoading = false;
        private bool IsDataLoaded = false;
        public string ResProviderName = "ZCloud File";
        public ZCloudFileProvider()
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
                Log.Information("[Res] [ZCloudFile] Loading core info");
                IsDataLoading = true;
                List<AListFileStructure> ZCloudFileInfo = await new AList().GetFileList(Host: "https://jn.sv.ztsin.cn:5244", Path: "MCSL2/MCSLAPI");

                foreach (AListFileStructure Result in ZCloudFileInfo)
                {
                    ZCloudFileResCoreItem CoreItem = new()
                    {
                        CoreName = Result.FileName,
                    };
                    CoreGridView.Items.Add(CoreItem);
                }
                IsDataLoading = false;
                IsDataLoaded = true;
                Log.Information($"[Res] [ZCloudFile] Core info loaded. Count: {ZCloudFileInfo.Count}");
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
            if (CoreGridView.SelectedIndex == -1)
            {
                return;
            }
            var SelectedCore = (ZCloudFileResCoreItem)CoreGridView.SelectedItem;
            Log.Information($"[Res] [ZCloudFile] Selected core \"{SelectedCore.CoreName}\"");
            CurrentCoreName.Text = SelectedCore.CoreName;
            CoreGridView.IsEnabled = false;
            try
            {
                List<AListFileStructure> ZCloudFileInfo = await new AList().GetFileList(Host: "https://jn.sv.ztsin.cn:5244", Path: $"MCSL2/MCSLAPI/{SelectedCore.CoreName}");
                ZCloudFileInfo.Reverse();
                CoreVersionStackPanel.Children.Clear();
                foreach (AListFileStructure Detail in ZCloudFileInfo)
                {
                    ZCloudFileResCoreVersionItem CoreDetailItem = new()
                    {
                        Core = SelectedCore.CoreName,
                        FileName = Detail.FileName,
                        FileSize = $"{(int.Parse(Detail.FileSize) / 1024.00 / 1024.00).ToString("0.00")} MB",
                    };
                    CoreVersionStackPanel.Children.Add(CoreDetailItem);
                }
                Log.Information($"[Res] [ZCloudFile] Core list loaded. Count: {ZCloudFileInfo.Count}");
            }
            catch (Exception ex)
            {
                Log.Error($"[Res] [ZCloudFile] Failed to get core list of \"{SelectedCore.CoreName}\". Reason: {ex.Message}");
            }
            CoreGridView.IsEnabled = true;
        }
    }
}
