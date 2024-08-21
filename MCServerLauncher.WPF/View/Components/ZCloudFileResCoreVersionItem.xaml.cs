using MCServerLauncher.WPF.Modules.Download;
using System.Threading.Tasks;

namespace MCServerLauncher.WPF.View.Components
{
    /// <summary>
    ///     ZCloudFileResCoreVersionItem.xaml 的交互逻辑
    /// </summary>
    public partial class ZCloudFileResCoreVersionItem
    {
        public ZCloudFileResCoreVersionItem()
        {
            InitializeComponent();
        }
        /// <summary>
        /// Core name.
        /// </summary>
        public string Core { get; set; }

        /// <summary>
        /// Core version.
        /// </summary>
        public string FileName
        {
            get => FileNameReplacer.Text;
            set => FileNameReplacer.Text = value;
        }

        /// <summary>
        /// File size.
        /// </summary>
        public string FileSize
        {
            get => FileSizeReplacer.Text;
            set => FileSizeReplacer.Text = value;
        }

        /// <summary>
        /// Get raw download url.
        /// Only use this when downloading, cuz it takes much time.
        /// </summary>
        public async Task<string> FetchRawUrl()
        {
            return await new AList().GetFileUrl("https://jn.sv.ztsin.cn:5244", $"MCSL2/MCSLAPI/{Core}/{FileName}");
        }
    }
}