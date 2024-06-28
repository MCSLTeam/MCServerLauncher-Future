using MCServerLauncher.UI.Modules.Download;
using System.Collections.Generic;
using System.Windows.Controls;
using static MCServerLauncher.UI.Modules.Download.FastMirror;
using  MCServerLauncher.UI.View.Components;

namespace MCServerLauncher.UI.View.ResDownloadProvider
{
    /// <summary>
    /// FastMirrorProvider.xaml 的交互逻辑
    /// </summary>
    public partial class FastMirrorProvider : UserControl
    {
        private bool IsLoading = false;
        private bool IsLoaded = false;
        private List<FastMirrorCoreInfo> FastMirrorInfo = new();

        public FastMirrorProvider()
        {
            InitializeComponent();
        }
        public async void Refresh()
        {
            if (IsLoading || IsLoaded)
            {
                return;
            }
            FastMirrorInfo = await new FastMirror().GetCoreInfo();
            foreach (FastMirrorCoreInfo Result in FastMirrorInfo)
            {
                FastMirrorResCoreItem CoreItem = new();
                CoreItem.CoreName = Result.Name;
                CoreItem.CoreTag = Result.Tag;
                CoreItem.Recommend = Result.Recommend;
                CoreGridView.Items.Add(CoreItem);
            }
        }
    }
}
