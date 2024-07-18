using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Collections.Generic;

namespace MCServerLauncher.WPF.View.Components
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
