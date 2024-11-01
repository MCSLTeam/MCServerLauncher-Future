using System.Windows;
using MCServerLauncher.WPF.Modules;
using MCServerLauncher.WPF.Modules.DownloadProvider;

namespace MCServerLauncher.WPF.View.Components.ResDownloadItem
{
    /// <summary>
    ///    MSLAPIResCoreVersionItem.xaml 的交互逻辑
    /// </summary>
    public partial class MSLAPIResCoreVersionItem
    {
        public MSLAPIResCoreVersionItem()
        {
            InitializeComponent();
        }

        /// <summary>
        ///    Minecraft version.
        /// </summary>
        public string MinecraftVersion
        {
            get => MinecraftVersionReplacer.Text;
            set => MinecraftVersionReplacer.Text = value;
        }

        /// <summary>
        ///    Raw API core name.
        /// </summary>
        public string? ApiActualName { get; set; }

        /// <summary>
        /// Download file.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void Download(object sender, RoutedEventArgs e)
        {
            var downloadUrl = await new MSLAPI().GetDownloadUrl(ApiActualName, MinecraftVersion);
            string defaultFileName = $"{ApiActualName}-{MinecraftVersion}.jar";
            await new DownloadManager().TriggerPreDownloadFile(downloadUrl, defaultFileName);
        }
    }
}