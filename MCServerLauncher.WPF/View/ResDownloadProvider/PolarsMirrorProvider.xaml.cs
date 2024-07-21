using MCServerLauncher.WPF.Modules.Download;
using MCServerLauncher.WPF.View.Components;
using Serilog;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using static MCServerLauncher.WPF.Modules.Download.PolarsMirror;

namespace MCServerLauncher.WPF.View.ResDownloadProvider
{
    /// <summary>
    /// PolarsMirrorProvider.xaml 的交互逻辑
    /// </summary>
    public partial class PolarsMirrorProvider : UserControl
    {
        private bool IsDataLoading = false;
        private bool IsDataLoaded = false;
        public string ResProviderName = "极星云镜像";
        public PolarsMirrorProvider()
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
                Log.Information("[Res] [PolarsMirror] Loading core info");
                IsDataLoading = true;
                List<PolarsMirrorCoreInfo> PolarsMirrorInfo = await new PolarsMirror().GetCoreInfo();

                foreach (PolarsMirrorCoreInfo Result in PolarsMirrorInfo)
                {
                    PolarsMirrorResCoreItem CoreItem = new()
                    {
                        CoreName = Result.Name,
                        CoreId = Result.Id,
                        CoreDescription = Result.Description,
                        CoreIconUrl = Result.IconUrl,
                    };
                    CoreGridView.Items.Add(CoreItem);
                }

                IsDataLoading = false;
                IsDataLoaded = true;
                Log.Information($"[Res] [PolarsMirror] Core info loaded. Count: {PolarsMirrorInfo.Count}");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[Res] [PolarsMirror] Failed to load core info. Reason: {ex.Message}");
                return false;
            }
        }
        private async void SetCore(object sender, SelectionChangedEventArgs e)
        {
            if (CoreGridView.SelectedIndex == -1)
            {
                return;
            }
            var SelectedCore = (PolarsMirrorResCoreItem)CoreGridView.SelectedItem;
            Log.Information($"[Res] [PolarsMirror] Selected core \"{SelectedCore.CoreName}\"");
            CurrentCoreName.Text = SelectedCore.CoreName;
            CurrentCoreDescription.Text = SelectedCore.CoreDescription;
            CurrentCoreIcon.Source = BitmapFrame.Create(new Uri(SelectedCore.CoreIconUrl), BitmapCreateOptions.None, BitmapCacheOption.Default);
            CoreGridView.IsEnabled = false;
            try
            {
                List<PolarsMirrorCoreDetail> PolarsMirrorCoreDetails = await new PolarsMirror().GetCoreDetail(SelectedCore.CoreId);
                CoreVersionStackPanel.Children.Clear();
                CoreGridView.IsEnabled = false;
                foreach (PolarsMirrorCoreDetail Detail in PolarsMirrorCoreDetails)
                {
                    PolarsMirrorResCoreVersionItem CoreDetailItem = new()
                    {
                        FileName = Detail.FileName,
                    };
                    CoreVersionStackPanel.Children.Add(CoreDetailItem);
                }
                Log.Information($"[Res] [PolarsMirror] Core detail loaded. Count: {PolarsMirrorCoreDetails.Count}");
            }
            catch (Exception ex)
            {
                Log.Error($"[Res] [PolarsMirror] Failed to get core detail of \"{SelectedCore.CoreName}\". Reason: {ex.Message}");
            }
            CoreGridView.IsEnabled = true;
        }

    }
}