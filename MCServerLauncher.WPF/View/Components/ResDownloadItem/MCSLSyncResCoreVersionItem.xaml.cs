using MCServerLauncher.WPF.Modules.Download;
using System.Threading.Tasks;

namespace MCServerLauncher.WPF.View.Components.ResDownloadItem
{
    /// <summary>
    ///     MCSLSyncResCoreVersionItem.xaml 的交互逻辑
    /// </summary>
    public partial class MCSLSyncResCoreVersionItem
    {
        public MCSLSyncResCoreVersionItem()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Core name.
        /// </summary>
        public string Core { get; set; }

        /// <summary>
        /// Minecraft version.
        /// </summary>
        public string MinecraftVersion
        {
            get => MinecraftVersionReplacer.Text;
            set => MinecraftVersionReplacer.Text = value;
        }

        /// <summary>
        /// Core version.
        /// </summary>
        public string CoreVersion
        {
            get => CoreVersionReplacer.Text;
            set => CoreVersionReplacer.Text = value;
        }

        /// <summary>
        /// Download URL.
        /// </summary>
        public async Task<string> GetDownloadUrl()
        {
            var coreDetail = await new MCSLSync().GetCoreDetail(Core, MinecraftVersion, CoreVersion);
            return coreDetail.DownloadUrl;
        }
    }
}