using Page = System.Windows.Controls.Page;
using MCServerLauncher.UI.View.ResDownloadProvider;
using iNKORE.UI.WPF.Modern.Controls;
using static MCServerLauncher.UI.Helpers.VisualTreeExtensions;
using System;

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

        }
        public void Refresh()
        {
            ContentDialog dialog = new();
            dialog.FullSizeDesired = false;
            ProgressRing ProgressRing = new();
            ProgressRing.IsActive = true;
            ProgressRing.Width = ProgressRing.Height = 50;
            dialog.Content = ProgressRing;
            try { dialog.ShowAsync(); } catch (Exception) { }
            FastMirror.Refresh();
            dialog.Hide();
        }
    }
}
