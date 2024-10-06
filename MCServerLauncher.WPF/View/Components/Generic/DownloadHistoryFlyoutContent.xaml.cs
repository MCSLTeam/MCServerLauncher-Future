namespace MCServerLauncher.WPF.View.Components.Generic
{
    /// <summary>
    ///     DownloadHistoryFlyoutContent.xaml 的交互逻辑
    /// </summary>
    public partial class DownloadHistoryFlyoutContent
    {
        private static DownloadHistoryFlyoutContent? _instance;

        public static DownloadHistoryFlyoutContent Instance
        {
            get
            {
                _instance ??= new DownloadHistoryFlyoutContent();
                return _instance;
            }
        }

        private DownloadHistoryFlyoutContent()
        {
            InitializeComponent();
        }
    }
}
