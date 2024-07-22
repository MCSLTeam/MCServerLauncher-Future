using System.Windows.Controls;

namespace MCServerLauncher.WPF.Main.View.Components
{
    /// <summary>
    /// ZCloudFileResCoreItem.xaml 的交互逻辑
    /// </summary>
    public partial class ZCloudFileResCoreItem : UserControl
    {
        public ZCloudFileResCoreItem()
        {
            InitializeComponent();
        }
        public string CoreName
        {
            get => CoreNameReplacer.Text;
            set => CoreNameReplacer.Text = value;
        }
    }
}
