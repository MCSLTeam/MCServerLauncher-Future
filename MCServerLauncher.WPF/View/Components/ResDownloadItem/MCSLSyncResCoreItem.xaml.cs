namespace MCServerLauncher.WPF.View.Components.ResDownloadItem
{
    /// <summary>
    ///     MCSLSyncResCoreItem.xaml 的交互逻辑
    /// </summary>
    public partial class MCSLSyncResCoreItem
    {
        public MCSLSyncResCoreItem()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Core name.
        /// </summary>
        public string CoreName
        {
            get => CoreNameReplacer.Text;
            set => CoreNameReplacer.Text = value;
        }
    }
}