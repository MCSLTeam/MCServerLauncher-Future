using MCServerLauncher.UI.Modules.Download;
using MCServerLauncher.UI.View.Components;
using Serilog;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using static MCServerLauncher.UI.Modules.Download.PolarsMirror;

namespace MCServerLauncher.UI.View.ResDownloadProvider
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
            } catch (Exception ex)
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
            PolarsMirrorResCoreItem CurrentCore = (PolarsMirrorResCoreItem)CoreGridView.SelectedItem;
            CurrentCoreName.Text = CurrentCore.CoreName;
            CurrentCoreDescription.Text = CurrentCore.CoreDescription;
            CurrentCoreIcon.Source = BitmapFrame.Create(new Uri(CurrentCore.CoreIconUrl), BitmapCreateOptions.None, BitmapCacheOption.Default);
            try
            {
                List<PolarsMirrorCoreDetail> FastMirrorCoreDetails = await new PolarsMirror().GetCoreDetail(CurrentCore.CoreId);
                CoreVersionStackPanel.Children.Clear();
                foreach (PolarsMirrorCoreDetail Detail in FastMirrorCoreDetails)
                {
                    PolarsMirrorResCoreVersionItem CoreDetailItem = new()
                    { 
                        FileName = Detail.FileName,
                    };
                    CoreVersionStackPanel.Children.Add(CoreDetailItem);
                }
                Log.Information($"[Res] [PolarsMirror] Core detail loaded. Count: {FastMirrorCoreDetails.Count}");
            }
            catch (Exception ex)
            {
                Log.Error($"[Res] [PolarsMirror] Failed to get core detail of \"{CurrentCore.CoreName}\". Reason: {ex.Message}");
            }
        }

    }
}