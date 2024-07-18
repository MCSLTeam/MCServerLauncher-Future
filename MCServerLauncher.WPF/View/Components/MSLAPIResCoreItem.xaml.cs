using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Collections.Generic;

namespace MCServerLauncher.WPF.View.Components
{
    /// <summary>
    /// MSLAPIResCoreItem.xaml 的交互逻辑
    /// </summary>
    public partial class MSLAPIResCoreItem : UserControl
    {
        public MSLAPIResCoreItem()
        {
            InitializeComponent();
        }
        public string CoreName
        {
            get => CoreNameReplacer.Text;
            set => CoreNameReplacer.Text = value;
        }
        public string APIActualName { get; set; }
    }
}
