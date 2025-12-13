namespace MCServerLauncher.WPF.View.Components.ResDownloadItem
{
    /// <summary>
    ///    RainYunResCoreItem.xaml 的交互逻辑
    /// </summary>
    public partial class RainYunResCoreItem
    {
        public RainYunResCoreItem()
        {
            InitializeComponent();
        }

        /// <summary>
        ///    Core name.
        /// </summary>
        public string? CoreName
        {
            get => CoreNameReplacer.Text;
            set => CoreNameReplacer.Text = value;
        }
    }
}