namespace MCServerLauncher.WPF.View.Components.ResDownloadItem
{
    /// <summary>
    ///    ZCloudFileResCoreItem.xaml 的交互逻辑
    /// </summary>
    public partial class ZCloudFileResCoreItem
    {
        public ZCloudFileResCoreItem()
        {
            InitializeComponent();
        }

        /// <summary>
        ///    Core name.
        /// </summary>
        public string CoreName
        {
            get => CoreNameReplacer.Text;
            set => CoreNameReplacer.Text = value;
        }
    }
}