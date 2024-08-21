using System.Windows;
using System.Windows.Controls;
using iNKORE.UI.WPF.Modern.Media.Animation;
using MCServerLauncher.WPF.Helpers;
using MCServerLauncher.WPF.View.ResDownloadProvider;

namespace MCServerLauncher.WPF.View
{
    /// <summary>
    ///     ResDownloadPage.xaml 的交互逻辑
    /// </summary>
    public partial class ResDownloadPage
    {
        public readonly FastMirrorProvider FastMirror = new();
        public readonly MCSLSyncProvider MCSLSync = new();
        public readonly MSLAPIProvider MSLAPI = new();
        public readonly PolarsMirrorProvider PolarsMirror = new();
        public readonly ZCloudFileProvider ZCloudFile = new();

        public ResDownloadPage()
        {
            InitializeComponent();
            // Refresh trigger when page is visible
            IsVisibleChanged += (s, e) =>
            {
                if (IsVisible) Refresh();
            };
        }

        /// <summary>
        /// Refresh current download provider
        /// </summary>
        public async void Refresh()
        {
            switch (BasicUtils.AppSettings.Download.DownloadSource)
            { 
                case "FastMirror":
                    CurrentResDownloadProvider.Content = FastMirror;
                    Subtitle.Text = $"你想要的，这里都有。 ( 当前正在使用 {FastMirror.ResProviderName} 下载源 )";
                    await FastMirror.Refresh();
                    break;
                case "PolarsMirror":
                    CurrentResDownloadProvider.Content = PolarsMirror;
                    Subtitle.Text = $"你想要的，这里都有。 ( 当前正在使用 {PolarsMirror.ResProviderName} 下载源 )";
                    await PolarsMirror.Refresh();
                    break;
                case "ZCloudFile":
                    CurrentResDownloadProvider.Content = ZCloudFile;
                    Subtitle.Text = $"你想要的，这里都有。 ( 当前正在使用 {ZCloudFile.ResProviderName} 下载源 )";
                    await ZCloudFile.Refresh();
                    break;
                case "MSLAPI":
                    CurrentResDownloadProvider.Content = MSLAPI;
                    Subtitle.Text = $"你想要的，这里都有。 ( 当前正在使用 {MSLAPI.ResProviderName} 下载源 )";
                    await MSLAPI.Refresh();
                    break;
                case "MCSLSync":
                    CurrentResDownloadProvider.Content = MCSLSync;
                    Subtitle.Text = $"你想要的，这里都有。 ( 当前正在使用 {MCSLSync.ResProviderName} 下载源 )";
                    await MCSLSync.Refresh();
                    break;
            }
        }
    }
}