using MCServerLauncher.WPF.Modules.Download;
using System.Windows.Controls;

namespace MCServerLauncher.WPF.View.Components
{
    /// <summary>
    /// ZCloudFileResCoreVersionItem.xaml 的交互逻辑
    /// </summary>
    public partial class ZCloudFileResCoreVersionItem : UserControl
    {
        public ZCloudFileResCoreVersionItem()
        {
            InitializeComponent();
        }
        public string Core { get; set; }
        public string FileName
        {
            get => FileNameReplacer.Text;
            set => FileNameReplacer.Text = value;
        }
        public string FileSize
        {
            get => FileSizeReplacer.Text;
            set => FileSizeReplacer.Text = value;
        }
        public string RawUrl { get; set; }
        public async void FetchRawUrl()
        {
            RawUrl = await new AList().GetFileUrl(Host: "https://jn.sv.ztsin.cn:5244", Path: $"MCSL2/MCSLAPI/{Core}/{FileName}");
        }
    }
}
