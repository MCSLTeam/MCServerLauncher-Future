namespace MCServerLauncher.WPF.View.Components.Generic
{
    /// <summary>
    ///    DownloadProgressItem.xaml 的交互逻辑
    /// </summary>
    public partial class DownloadProgressItem
    {
        public DownloadProgressItem()
        {
            InitializeComponent();
        }

        /// <summary>
        ///    Downloading file name.
        /// </summary>
        public string FileName
        {
            get => DownloadFileName.Text;
            set => DownloadFileName.Text = value;
        }

        /// <summary>
        ///    Download progress.
        /// </summary>
        public int Progress
        {
            get => (int)DownloadProgressBar.Value;
            set => DownloadProgressBar.Value = value;
        }
    }
}