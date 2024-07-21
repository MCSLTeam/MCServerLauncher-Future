using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MCServerLauncher.WPF.View.Components
{
    /// <summary>
    /// FastMirrorResCoreItem.xaml 的交互逻辑
    /// </summary>
    public partial class FastMirrorResCoreItem : UserControl
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
        public FastMirrorResCoreItem()
        {
            InitializeComponent();
        }
        public string CoreName
        {
            get => CoreNameReplacer.Text;
            set => CoreNameReplacer.Text = value;
        }
        public string CoreTag
        {
            get => CoreTagReplacer.Text;
            set => CoreTagReplacer.Text = value;
        }
        public bool Recommend
        {
            get => GetRecommendStatus();
            set => SetRecommendStatus(value);
        }
        public string HomePage { get; set; }
        public List<string> MinecraftVersions { get; set; }
        private void SetRecommendStatus(bool RecommendStatus)
        {
            CoreItemBorder.Background = RecommendStatus ? RecommendBrush : null;
            if (RecommendStatus)
            {
                CoreNameReplacer.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ffffff"));
            }
        }
        private bool GetRecommendStatus()
        {
            return CoreItemBorder.Background == RecommendBrush;
        }
    }
}
