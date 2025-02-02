using MCServerLauncher.WPF.Modules;
using MCServerLauncher.WPF.Modules.DownloadProvider;
using System.Windows;

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
        public string? Core { get; set; }

        /// <summary>
        ///    Minecraft version.
        /// </summary>
        public string? MinecraftVersion
        {
            get => MinecraftVersionReplacer.Text;
            set => MinecraftVersionReplacer.Text = value;
        }

        /// <summary>
        ///    Core version.
        /// </summary>
        public string? CoreVersion
        {
            get => CoreVersionReplacer.Text;
            set => CoreVersionReplacer.Text = value;
        }

        /// <summary>
        /// Download file.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void Download(object sender, RoutedEventArgs e)
        {
            var downloadUrl = new FastMirror().CombineDownloadUrl(Core, MinecraftVersion, CoreVersion);
            string defaultFileName = $"{Core}-{MinecraftVersion}-{CoreVersion}.jar";
            await new DownloadManager().TriggerPreDownloadFile(downloadUrl, defaultFileName);
        }
    }
}