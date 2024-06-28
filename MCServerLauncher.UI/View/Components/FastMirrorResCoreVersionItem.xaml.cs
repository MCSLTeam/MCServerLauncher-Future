using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace MCServerLauncher.UI.View.Components
{
    /// <summary>
    /// FastMirrorResCoreVersionItem.xaml 的交互逻辑
    /// </summary>
    public partial class FastMirrorResCoreVersionItem : UserControl
    {
        public FastMirrorResCoreVersionItem()
        {
            InitializeComponent();
        }
        public string MinecraftVersion
        {
            get => MinecraftVersionReplacer.Text;
            set => MinecraftVersionReplacer.Text = value;
        }
        public string CoreVersion
        {
            get => CoreVersionReplacer.Text;
            set => CoreVersionReplacer.Text = value;
        }
    }
}
