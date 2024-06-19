using Page = System.Windows.Controls.Page;
using MCServerLauncher.UI.View.ResDownloadProvider;

namespace MCServerLauncher.UI.View
{
    /// <summary>
    /// ResDownloadPage.xaml 的交互逻辑
    /// </summary>
    public partial class ResDownloadPage : Page
    {
        public FastMirrorProvider fastMirror = new();
        public ResDownloadPage()
        {
            InitializeComponent();
            CurrentResDownloadProvider.Content = fastMirror;
        }
    }
}
