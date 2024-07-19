using Page = System.Windows.Controls.Page;
using MCServerLauncher.WPF.View.ResDownloadProvider;
using System.Windows;
using System.Windows.Controls;
using iNKORE.UI.WPF.Modern.Media.Animation;

namespace MCServerLauncher.WPF.View
{
    /// <summary>
    /// ResDownloadPage.xaml 的交互逻辑
    /// </summary>
    public partial class ResDownloadPage : Page
    {
        public FastMirrorProvider FastMirror = new();
        public PolarsMirrorProvider PolarsMirror = new();
        public MSLAPIProvider MSLAPI = new();
        public ZCloudFileProvider ZCloudFile = new();
        public MCSLSyncProvider MCSLSync = new();
        public ResDownloadPage()
        {
            InitializeComponent();
            CurrentResDownloadProvider.Content = FastMirror;
            Subtitle.Text += $" ( 当前正在使用 {FastMirror.ResProviderName} 下载源 )";
            //CurrentResDownloadProvider.Content = PolarsMirror;
            //Subtitle.Text += $" ( 当前正在使用 {PolarsMirror.ResProviderName} 下载源 )";
            //CurrentResDownloadProvider.Content = MSLAPI;
            //Subtitle.Text += $" ( 当前正在使用 {MSLAPI.ResProviderName} 下载源 )";
            //CurrentResDownloadProvider.Content = ZCloudFile;
            //Subtitle.Text += $" ( 当前正在使用 {ZCloudFile.ResProviderName} 下载源 )";
            //CurrentResDownloadProvider.Content = MCSLSync;
            //Subtitle.Text += $" ( 当前正在使用 {MCSLSync.ResProviderName} 下载源 )";
            IsVisibleChanged += (sender, e) => { if (IsVisible) Refresh(); };
        }
        public async void Refresh()
        {
            await FastMirror.Refresh();
            //await PolarsMirror.Refresh();
            //await MSLAPI.Refresh();
            //await ZCloudFile.Refresh();
            //await MCSLSync.Refresh();
        }
        private async void ChResDownloadSrc(object sender, RoutedEventArgs e)
        {
            switch ((sender as Button).Content)
                {
                case "FastMirror":
                    CurrentResDownloadProvider.Navigate(FastMirror, new DrillInNavigationTransitionInfo());
                    Subtitle.Text = $"你想要的，这里都有。 ( 当前正在使用 {FastMirror.ResProviderName} 下载源 )";
                    await FastMirror.Refresh();
                    break;
                case "PolarsMirror":
                    CurrentResDownloadProvider.Navigate(PolarsMirror, new DrillInNavigationTransitionInfo());
                    Subtitle.Text = $"你想要的，这里都有。 ( 当前正在使用 {PolarsMirror.ResProviderName} 下载源 )";
                    await PolarsMirror.Refresh();
                    break;
                case "MSLAPI":
                    CurrentResDownloadProvider.Navigate(MSLAPI, new DrillInNavigationTransitionInfo());
                    Subtitle.Text = $"你想要的，这里都有。 ( 当前正在使用 {MSLAPI.ResProviderName} 下载源 )";
                    await MSLAPI.Refresh();
                    break;
                case "ZCloudFile":
                    CurrentResDownloadProvider.Navigate(ZCloudFile, new DrillInNavigationTransitionInfo());
                    Subtitle.Text = $"你想要的，这里都有。 ( 当前正在使用 {ZCloudFile.ResProviderName} 下载源 )";
                    await ZCloudFile.Refresh();
                    break;
                case "MCSL-Sync":
                    CurrentResDownloadProvider.Navigate(MCSLSync, new DrillInNavigationTransitionInfo());
                    Subtitle.Text = $"你想要的，这里都有。 ( 当前正在使用 {MCSLSync.ResProviderName} 下载源 )";
                    await MCSLSync.Refresh();
                    break;
            }
        }
    }
}
