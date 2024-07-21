using System.Collections.Generic;
using System.Windows.Controls;

namespace MCServerLauncher.WPF.Main.View.Components
{
    /// <summary>
    /// PolarsMirrorResCoreItem.xaml 的交互逻辑
    /// </summary>
    public partial class PolarsMirrorResCoreItem : UserControl
    {
        public PolarsMirrorResCoreItem()
        {
            InitializeComponent();
        }
        public string CoreName
        {
            get => CoreNameReplacer.Text;
            set => CoreNameReplacer.Text = value;
        }
        public string CoreDescription { get; set; }
        public int CoreId { get; set; }
        public string CoreIconUrl { get; set; }
        public List<string> MinecraftVersions { get; set; }
    }
}
