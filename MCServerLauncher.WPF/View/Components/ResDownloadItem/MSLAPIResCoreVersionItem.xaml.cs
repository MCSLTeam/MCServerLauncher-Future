using iNKORE.UI.WPF.Modern.Controls;
using MCServerLauncher.Common.DownloadProvider;
using MCServerLauncher.WPF.Modules;
using System.Windows;

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
            var defaultFileName = GetDisplayFileName();
            if (string.IsNullOrWhiteSpace(ApiActualName))
            {
                NotifyDownloadFailed(defaultFileName);
                return;
            }

            var downloadUrl = await MSLAPI.GetDownloadUrl(ApiActualName, MinecraftVersion);
            if (string.IsNullOrWhiteSpace(downloadUrl))
            {
                NotifyDownloadFailed(defaultFileName);
                return;
            }

            await new DownloadManager().TriggerPreDownloadFile(downloadUrl, defaultFileName);
        }

        private string GetDisplayFileName()
        {
            return string.IsNullOrWhiteSpace(ApiActualName)
                ? $"{MinecraftVersion}.jar"
                : $"{ApiActualName}-{MinecraftVersion}.jar";
        }

        private static void NotifyDownloadFailed(string fileName)
        {
            Notification.Push(
                title: Lang.Tr["DownloadFailed"],
                message: $"{fileName} {Lang.Tr["DownloadFailed"]}",
                isClosable: true,
                severity: InfoBarSeverity.Error
            );
        }
    }
}
