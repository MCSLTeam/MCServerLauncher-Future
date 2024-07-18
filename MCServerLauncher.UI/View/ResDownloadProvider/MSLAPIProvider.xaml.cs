using MCServerLauncher.UI.Modules.Download;
using MCServerLauncher.UI.View.Components;
using Serilog;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace MCServerLauncher.UI.View.ResDownloadProvider
{
    /// <summary>
    /// MSLAPIProvider.xaml 的交互逻辑
    /// </summary>
    public partial class MSLAPIProvider : UserControl
    {
        private bool IsDataLoading = false;
        private bool IsDataLoaded = false;
        public string ResProviderName = "MSL";
        public MSLAPIProvider()
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
                Log.Information("[Res] [MSLAPI] Loading core info");
                IsDataLoading = true;
                List<string> MSLAPIInfo = await new MSLAPI().GetCoreInfo();
                if (MSLAPIInfo == null)
                {
                    Log.Error("[Res] [MSLAPI] Failed to load core info.");
                    return false;
                }
                foreach (string Result in MSLAPIInfo)
                {
                    CoreGridView.Items.Add(
                        new MSLAPIResCoreItem()
                        {
                            CoreName = new MSLAPI().SerializeCoreName(Result),
                            APIActualName = Result
                        }
                    );
                }

                IsDataLoading = false;
                IsDataLoaded = true;
                Log.Information($"[Res] [MSLAPI] Core info loaded. Count: {MSLAPIInfo.Count}");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[Res] [MSLAPI] Failed to load core info. Reason: {ex.Message}");
                return false;
            }
        }
        private async void SetCore(object sender, SelectionChangedEventArgs e)
        {
            if (CoreGridView.SelectedIndex == -1)
            {
                return;
            }
            var SelectedCore = (MSLAPIResCoreItem)CoreGridView.SelectedItem;
            Log.Information($"[Res] [MSLAPI] Selected core \"{SelectedCore.CoreName}\"");
            MSLAPIResCoreItem CurrentCore = (MSLAPIResCoreItem)CoreGridView.SelectedItem;
            CurrentCoreName.Text = CurrentCore.CoreName;
            CurrentCoreDescription.Text = await new MSLAPI().GetCoreDescription(CurrentCore.APIActualName);
            try
            {
                List<string> MSLAPICoreDetails = await new MSLAPI().GetMinecraftVersions(CurrentCore.APIActualName);
                CoreVersionStackPanel.Children.Clear();
                foreach (string Detail in MSLAPICoreDetails)
                {
                    MSLAPIResCoreVersionItem CoreDetailItem = new()
                    {
                        MinecraftVersion = Detail
                    };
                    CoreVersionStackPanel.Children.Add(CoreDetailItem);
                }
                Log.Information($"[Res] [MSLAPI] Core detail loaded. Count: {MSLAPICoreDetails.Count}");
            }
            catch (Exception ex)
            {
                Log.Error($"[Res] [MSLAPI] Failed to get core detail of \"{CurrentCore.CoreName}\". Reason: {ex.Message}");
            }
        }
    }
}
