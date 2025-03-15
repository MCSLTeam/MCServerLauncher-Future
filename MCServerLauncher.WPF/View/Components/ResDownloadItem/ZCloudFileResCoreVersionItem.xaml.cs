using MCServerLauncher.WPF.Modules;
using MCServerLauncher.WPF.Modules.DownloadProvider;
using System;
using System.Threading.Tasks;
using System.Windows;

namespace MCServerLauncher.WPF.View.Components.ResDownloadItem
{
    /// <summary>
    ///    ZCloudFileResCoreVersionItem.xaml 的交互逻辑
    /// </summary>
    public partial class ZCloudFileResCoreVersionItem
    {
        public ZCloudFileResCoreVersionItem()
        {
            InitializeComponent();
        }

        /// <summary>
        ///    Core name.
        /// </summary>
        public string? Core { get; set; }

        /// <summary>
        ///    Core version.
        /// </summary>
        public string? FileName
        {
            get => FileNameReplacer.Text;
            set => FileNameReplacer.Text = value;
        }

        /// <summary>
        ///    File size.
        /// </summary>
        public string FileSize
        {
            get => FileSizeReplacer.Text;
            set => FileSizeReplacer.Text = value;
        }

        /// <summary>
        ///    Get raw download url.
        ///    Only use this when downloading, cuz it takes much time.
        /// </summary>
        public async Task<string?> FetchRawUrl()
        {
            return await new AList().GetFileUrl("https://jn.sv.ztsin.cn:5244", $"MCSL2/MCSLAPI/{Core}/{FileName}");
        }

        /// <summary>
        ///   Download file.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void Download(object sender, RoutedEventArgs e)
        {
            var downloadUrl = await FetchRawUrl();
            if (downloadUrl == null)
            {
                throw new ArgumentNullException(nameof(downloadUrl));
            }
            if (FileName == null)
            {
                throw new ArgumentNullException(nameof(FileName));
            }
            await new DownloadManager().TriggerPreDownloadFile(downloadUrl: downloadUrl, defaultFileName: FileName);
        }
    }
}