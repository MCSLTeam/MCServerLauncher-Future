using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;
using MCServerLauncher.WPF.Modules;
using MCServerLauncher.WPF.Modules.DownloadProvider;
using MCServerLauncher.WPF.View.Components.ResDownloadItem;
using MCServerLauncher.WPF.View.Pages;
using Serilog;

namespace MCServerLauncher.WPF.View.ResDownloadProvider
{
    /// <summary>
    ///    MSLAPIProvider.xaml 的交互逻辑
    /// </summary>
    public partial class MSLAPIProvider : IResDownloadProvider
    {
        public string ResProviderName => "MSL";
        private bool _isDataLoaded;
        private bool _isDataLoading;

        public MSLAPIProvider()
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
                Log.Information("[Res] [MSLAPI] Loading core info");

                CoreGridView.Items.Clear();
                CoreVersionStackPanel.Children.Clear();
                CurrentCoreName.Text = string.Empty;
                CurrentCoreDescription.Text = string.Empty;
                IsEnabled = false;

                _isDataLoading = true;
                var mslapiInfo = await new MSLAPI().GetCoreInfo();
                if (mslapiInfo == null)
                {
                    Log.Error("[Res] [MSLAPI] Failed to load core info.");
                    return false;
                }

                foreach (var result in mslapiInfo)
                    CoreGridView.Items.Add(
                        new MSLAPIResCoreItem
                        {
                            CoreName = MSLAPI.SerializeCoreName(result),
                            ApiActualName = result
                        }
                    );

                IsEnabled = true;

                _isDataLoading = false;
                _isDataLoaded = true;
                Log.Information($"[Res] [MSLAPI] Core info loaded. Count: {mslapiInfo.Count}");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[Res] [MSLAPI] Failed to load core info. Reason: {ex.Message}");
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
            var selectedCore = (MSLAPIResCoreItem)CoreGridView.SelectedItem;
            Log.Information($"[Res] [MSLAPI] Selected core \"{selectedCore.CoreName}\"");
            CurrentCoreName.Text = selectedCore.CoreName;
            CurrentCoreDescription.Text = await new MSLAPI().GetCoreDescription(selectedCore.ApiActualName);
            CoreGridView.IsEnabled = false;
            try
            {
                var mslapiCoreDetails = await new MSLAPI().GetMinecraftVersions(selectedCore.ApiActualName);
                CoreVersionStackPanel.Children.Clear();
                if (mslapiCoreDetails != null)
                {
                    foreach (var coreDetailItem in mslapiCoreDetails.Select(
                        detail => new MSLAPIResCoreVersionItem
                        {
                            ApiActualName = selectedCore.ApiActualName,
                            MinecraftVersion = detail
                        }
                        )
                    )
                        CoreVersionStackPanel.Children.Add(coreDetailItem);

                    Log.Information($"[Res] [MSLAPI] Core detail loaded. Count: {mslapiCoreDetails.Count}");
                }
            }
            catch (Exception ex)
            {
                Log.Error(
                    $"[Res] [MSLAPI] Failed to get core detail of \"{selectedCore.CoreName}\". Reason: {ex.Message}");
            }

            CoreGridView.IsEnabled = true;
        }
    }
}