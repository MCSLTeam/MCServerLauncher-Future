namespace MCServerLauncher.WPF.Main.View.Components
{
    /// <summary>
    ///     FastMirrorResCoreVersionItem.xaml 的交互逻辑
    /// </summary>
    public partial class FastMirrorResCoreVersionItem
    {
        public FastMirrorResCoreVersionItem()
        {
            InitializeComponent();
        }

        public string Core { get; set; }

        public string MinecraftVersion
        {
            get => MinecraftVersionReplacer.Text;
            set => MinecraftVersionReplacer.Text = value;
        }

        public string CoreVersion
        {
            get => CoreVersionReplacer.Text;
            set => CoreVersionReplacer.Text = value;
        }
    }
}