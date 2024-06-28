using Page = System.Windows.Controls.Page;
using MCServerLauncher.UI.View.ResDownloadProvider;
using iNKORE.UI.WPF.Modern.Controls;
using IconAndText = iNKORE.UI.WPF.Modern.Controls.IconAndText;
using System;
using iNKORE.UI.WPF.Modern.Common.IconKeys;
using System.Threading.Tasks;

namespace MCServerLauncher.UI.View
{
    /// <summary>
    /// ResDownloadPage.xaml 的交互逻辑
    /// </summary>
    public partial class ResDownloadPage : Page
    {
        public FastMirrorProvider FastMirror = new();
        public ResDownloadPage()
        {
            InitializeComponent();
            CurrentResDownloadProvider.Content = FastMirror;
            IsVisibleChanged += (sender, e) => { if (IsVisible) Refresh(); };
        }
        public async void Refresh()
        {
            await FastMirror.Refresh();
        }
    }
}
