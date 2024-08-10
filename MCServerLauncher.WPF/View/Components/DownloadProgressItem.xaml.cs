namespace MCServerLauncher.WPF.View.Components
{
    /// <summary>
    ///     DownloadProgressItem.xaml 的交互逻辑
    /// </summary>
    public partial class DownloadProgressItem
    {
        public DownloadProgressItem()
        {
            InitializeComponent();
        }
        public string FileName
        {
            get => DownloadFileName.Text;
            set => DownloadFileName.Text = value;
        }
        public int Progress
        {
            get => (int)DownloadProgressBar.Value;
            set => DownloadProgressBar.Value = value;
        }
    }
}
