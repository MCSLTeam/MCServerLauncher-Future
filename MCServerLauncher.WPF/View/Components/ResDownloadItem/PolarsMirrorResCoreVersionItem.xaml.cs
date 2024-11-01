using System.Windows;
using MCServerLauncher.WPF.Modules;
using MCServerLauncher.WPF.Modules.DownloadProvider;

namespace MCServerLauncher.WPF.View.Components.ResDownloadItem
{
    /// <summary>
    ///    PolarsMirrorResCoreVersionItem.xaml 的交互逻辑
    /// </summary>
    public partial class PolarsMirrorResCoreVersionItem
    {
        public PolarsMirrorResCoreVersionItem()
        {
            InitializeComponent();
        }

        /// <summary>
        ///    File name.
        /// </summary>
        public string? FileName
        {
            get => FileNameReplacer.Text;
            set => FileNameReplacer.Text = value;
        }

        /// <summary>
        ///   Download URL.
        /// </summary>
        public string? DownloadUrl { get; set; }

        /// <summary>
        ///   Download file.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void Download(object sender, RoutedEventArgs e)
        {
            await new DownloadManager().TriggerPreDownloadFile(DownloadUrl, FileName);
        }
    }
}