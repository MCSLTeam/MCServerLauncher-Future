using MCServerLauncher.WPF.Modules;
using MCServerLauncher.WPF.Modules.DownloadProvider;
using MCServerLauncher.WPF.View.Components.ResDownloadItem;
using MCServerLauncher.WPF.View.Pages;
using Serilog;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace MCServerLauncher.WPF.View.ResDownloadProvider
{
    /// <summary>
    ///    RainYunProvider.xaml 的交互逻辑
    /// </summary>
    public partial class RainYunProvider : IResDownloadProvider
    {
        public string ResProviderName => "RainYun";
        private bool _isDataLoaded;
        private bool _isDataLoading;

        public RainYunProvider()
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
                Log.Information("[Res] [RainYun] Loading core info");

                CoreGridView.Items.Clear();
                CoreVersionStackPanel.Children.Clear();
                CurrentCoreName.Text = string.Empty;
                IsEnabled = false;

                _isDataLoading = true;
                var ryFileInfo = await new AList().GetFileList("https://mirrors.rainyun.com", "服务端合集");

                foreach (var coreItem in ryFileInfo.Select(result => new RainYunResCoreItem
                {
                    CoreName = result.FileName
                }))
                    CoreGridView.Items.Add(coreItem);

                IsEnabled = true;

                _isDataLoading = false;
                _isDataLoaded = true;
                if (ryFileInfo != null)
                    Log.Information($"[Res] [RainYun] Core info loaded. Count: {ryFileInfo.Count}");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[Res] [RainYun] Failed to load core info. Reason: {ex.Message}");
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
            var selectedCore = (RainYunResCoreItem)CoreGridView.SelectedItem;
            Log.Information($"[Res] [RainYun] Selected core \"{selectedCore.CoreName}\"");
            CurrentCoreName.Text = selectedCore.CoreName;
            CoreGridView.IsEnabled = false;
            try
            {
                var ryFileInfo = await new AList().GetFileList("https://mirrors.rainyun.com",
                    $"服务端合集/{selectedCore.CoreName}");
                ryFileInfo?.Reverse();
                CoreVersionStackPanel.Children.Clear();
                foreach (var coreDetailItem in (ryFileInfo ?? throw new InvalidOperationException()).Select(detail => new RainYunResCoreVersionItem
                {
                    Core = selectedCore.CoreName,
                    FileName = detail.FileName,
                    FileSize = $"{int.Parse(detail.FileSize ?? throw new InvalidOperationException()) / 1024.00 / 1024.00:0.00} MB"
                }))
                    CoreVersionStackPanel.Children.Add(coreDetailItem);

                Log.Information($"[Res] [RainYun] Core list loaded. Count: {ryFileInfo.Count}");
            }
            catch (Exception ex)
            {
                Log.Error(
                    $"[Res] [RainYun] Failed to get core list of \"{selectedCore.CoreName}\". Reason: {ex.Message}");
            }

            CoreGridView.IsEnabled = true;
        }
    }
}