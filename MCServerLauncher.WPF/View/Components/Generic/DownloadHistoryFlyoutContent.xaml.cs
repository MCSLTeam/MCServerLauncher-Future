namespace MCServerLauncher.WPF.View.Components.Generic
{
    /// <summary>
    ///     DownloadHistoryFlyoutContent.xaml 的交互逻辑
    /// </summary>
    public partial class DownloadHistoryFlyoutContent
    {
        private static DownloadHistoryFlyoutContent instance;

        public static DownloadHistoryFlyoutContent Instance
        {
            get
            {
                instance ??= new DownloadHistoryFlyoutContent();
                return instance;
            }
        }

        private DownloadHistoryFlyoutContent()
        {
            InitializeComponent();
        }
    }
}
