using MCServerLauncher.WPF.Main.Modules.Download;

namespace MCServerLauncher.WPF.Main.View.Components
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

        public string RawUrl { get; set; }

        //public string SHA1 { get; set; }
        public async void FetchCoreDetail()
        {
            var CoreDetail = await new MCSLSync().GetCoreDetail(Core, MinecraftVersion, CoreVersion);
            RawUrl = CoreDetail.DownloadUrl;
            //SHA1 = CoreDetail.SHA1;
        }
    }
}