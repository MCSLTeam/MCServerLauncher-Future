namespace MCServerLauncher.WPF.View.Components.ResDownloadItem
{
    /// <summary>
    ///     MSLAPIResCoreVersionItem.xaml 的交互逻辑
    /// </summary>
    public partial class MSLAPIResCoreVersionItem
    {
        public MSLAPIResCoreVersionItem()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Minecraft version.
        /// </summary>
        public string MinecraftVersion
        {
            get => MinecraftVersionReplacer.Text;
            set => MinecraftVersionReplacer.Text = value;
        }
    }
}