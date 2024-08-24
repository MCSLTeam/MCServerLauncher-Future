using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using MCServerLauncher.WPF.Helpers;
using MCServerLauncher.WPF.Modules.Download;
using MCServerLauncher.WPF.View.Components.ResDownloadItem;
using Serilog;

namespace MCServerLauncher.WPF.View.ResDownloadProvider
{
    /// <summary>
    ///    PolarsMirrorProvider.xaml 的交互逻辑
    /// </summary>
    public partial class PolarsMirrorProvider
    {
        public readonly string ResProviderName = LanguageManager.Instance["ResDownloadPage_ProviderName_PolarsMirror"];
        private bool _isDataLoaded;
        private bool _isDataLoading;

        public PolarsMirrorProvider()
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
                Log.Information("[Res] [PolarsMirror] Loading core info");
                _isDataLoading = true;
                var polarsMirrorInfo = await new PolarsMirror().GetCoreInfo();

                foreach (var coreItem in polarsMirrorInfo.Select(result => new PolarsMirrorResCoreItem
                         {
                             CoreName = result.Name,
                             CoreId = result.Id,
                             CoreDescription = result.Description,
                             CoreIconUrl = result.IconUrl
                         }))
                    CoreGridView.Items.Add(coreItem);

                _isDataLoading = false;
                _isDataLoaded = true;
                Log.Information($"[Res] [PolarsMirror] Core info loaded. Count: {polarsMirrorInfo.Count}");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[Res] [PolarsMirror] Failed to load core info. Reason: {ex.Message}");
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
            var selectedCore = (PolarsMirrorResCoreItem)CoreGridView.SelectedItem;
            Log.Information($"[Res] [PolarsMirror] Selected core \"{selectedCore.CoreName}\"");
            CurrentCoreName.Text = selectedCore.CoreName;
            CurrentCoreDescription.Text = selectedCore.CoreDescription;
            CurrentCoreIcon.Source = BitmapFrame.Create(new Uri(selectedCore.CoreIconUrl), BitmapCreateOptions.None,
                BitmapCacheOption.Default);
            CoreGridView.IsEnabled = false;
            try
            {
                var polarsMirrorCoreDetails = await new PolarsMirror().GetCoreDetail(selectedCore.CoreId);
                CoreVersionStackPanel.Children.Clear();
                CoreGridView.IsEnabled = false;
                foreach (var coreDetailItem in polarsMirrorCoreDetails.Select(detail =>
                             new PolarsMirrorResCoreVersionItem
                             {
                                 FileName = detail.FileName
                             }))
                    CoreVersionStackPanel.Children.Add(coreDetailItem);

                Log.Information($"[Res] [PolarsMirror] Core detail loaded. Count: {polarsMirrorCoreDetails.Count}");
            }
            catch (Exception ex)
            {
                Log.Error(
                    $"[Res] [PolarsMirror] Failed to get core detail of \"{selectedCore.CoreName}\". Reason: {ex.Message}");
            }

            CoreGridView.IsEnabled = true;
        }
    }
}