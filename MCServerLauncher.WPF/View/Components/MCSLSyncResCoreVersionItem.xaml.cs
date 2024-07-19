using MCServerLauncher.WPF.Modules.Download;
using System.Windows.Controls;
using static MCServerLauncher.WPF.Modules.Download.MCSLSync;

namespace MCServerLauncher.WPF.View.Components
{
    /// <summary>
    /// MCSLSyncResCoreVersionItem.xaml 的交互逻辑
    /// </summary>
    public partial class MCSLSyncResCoreVersionItem : UserControl
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
            MCSLSyncCoreDetail CoreDetail = await new MCSLSync().GetCoreDetail(Core, MinecraftVersion, CoreVersion);
            RawUrl = CoreDetail.DownloadUrl;
            //SHA1 = CoreDetail.SHA1;
        }
    }
}
