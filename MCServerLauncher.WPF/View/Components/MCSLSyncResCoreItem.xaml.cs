using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Collections.Generic;

namespace MCServerLauncher.WPF.View.Components
{
    /// <summary>
    /// MCSLSyncResCoreItem.xaml 的交互逻辑
    /// </summary>
    public partial class MCSLSyncResCoreItem : UserControl
    {
        public MCSLSyncResCoreItem()
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
