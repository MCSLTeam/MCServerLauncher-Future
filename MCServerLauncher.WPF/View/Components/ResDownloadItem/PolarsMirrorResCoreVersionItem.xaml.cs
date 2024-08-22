namespace MCServerLauncher.WPF.View.Components.ResDownloadItem
{
    /// <summary>
    ///     PolarsMirrorResCoreVersionItem.xaml 的交互逻辑
    /// </summary>
    public partial class PolarsMirrorResCoreVersionItem
    {
        public PolarsMirrorResCoreVersionItem()
        {
            InitializeComponent();
        }

        /// <summary>
        /// File name.
        /// </summary>
        public string FileName
        {
            get => FileNameReplacer.Text;
            set => FileNameReplacer.Text = value;
        }
    }
}