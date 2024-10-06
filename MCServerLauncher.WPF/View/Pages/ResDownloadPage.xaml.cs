using MCServerLauncher.WPF.Modules;
using MCServerLauncher.WPF.View.ResDownloadProvider;

namespace MCServerLauncher.WPF.View.Pages
{
    /// <summary>
    ///    ResDownloadPage.xaml 的交互逻辑
    /// </summary>
    public partial class ResDownloadPage
    {
        public readonly FastMirrorProvider? FastMirror = new();
        public readonly MCSLSyncProvider? MCSLSync = new();
        public readonly MSLAPIProvider? MSLAPI = new();
        public readonly PolarsMirrorProvider? PolarsMirror = new();
        public readonly ZCloudFileProvider? ZCloudFile = new();

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
        ///    Refresh current download provider
        /// </summary>
        public async void Refresh()
        {
            IResDownloadProvider? currentResDownloadProvider = SettingsManager.AppSettings?.Download?.DownloadSource switch
            {
                "FastMirror" => FastMirror,
                "PolarsMirror" => PolarsMirror,
                "ZCloudFile" => ZCloudFile,
                "MSLAPI" => MSLAPI,
                "MCSLSync" => MCSLSync,
                _ => null
            };
            Subtitle.Text = $"{LanguageManager.Localize["ResDownloadTipPrefix"]} {currentResDownloadProvider!.ResProviderName} {LanguageManager.Localize["ResDownloadTipSuffix"]}";
            CurrentResDownloadProvider.Content = currentResDownloadProvider;
            await currentResDownloadProvider.Refresh();
        }
    }
}