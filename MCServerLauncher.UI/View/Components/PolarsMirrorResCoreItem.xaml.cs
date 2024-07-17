using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Collections.Generic;

namespace MCServerLauncher.UI.View.Components
{
    /// <summary>
    /// PolarsMirrorResCoreItem.xaml 的交互逻辑
    /// </summary>
    public partial class PolarsMirrorResCoreItem : UserControl
    {
        private LinearGradientBrush RecommendBrush = new LinearGradientBrush(
            new GradientStopCollection()
            {
                new GradientStop((Color)ColorConverter.ConvertFromString("#f3bc00"), 0),
                new GradientStop((Color)ColorConverter.ConvertFromString("#ef9500"), 1)
            },
            new Point(0, 0),
            new Point(0.5, 0.85)
        );
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
