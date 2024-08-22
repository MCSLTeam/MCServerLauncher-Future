namespace MCServerLauncher.WPF.View.Components.ResDownloadItem
{
    /// <summary>
    ///    FastMirrorResCoreVersionItem.xaml 的交互逻辑
    /// </summary>
    public partial class FastMirrorResCoreVersionItem
    {
        public FastMirrorResCoreVersionItem()
        {
            InitializeComponent();
        }

        /// <summary>
        ///    Core name.
        /// </summary>
        public string Core { get; set; }

        /// <summary>
        ///    Minecraft version.
        /// </summary>
        public string MinecraftVersion
        {
            get => MinecraftVersionReplacer.Text;
            set => MinecraftVersionReplacer.Text = value;
        }

        /// <summary>
        ///    Core version.
        /// </summary>
        public string CoreVersion
        {
            get => CoreVersionReplacer.Text;
            set => CoreVersionReplacer.Text = value;
        }
    }
}