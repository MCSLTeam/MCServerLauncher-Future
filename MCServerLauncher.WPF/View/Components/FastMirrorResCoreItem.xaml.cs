using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace MCServerLauncher.WPF.View.Components
{
    /// <summary>
    ///     FastMirrorResCoreItem.xaml 的交互逻辑
    /// </summary>
    public partial class FastMirrorResCoreItem
    {
        private readonly LinearGradientBrush _recommendBrush = new(
            new GradientStopCollection
            {
                new((Color)ColorConverter.ConvertFromString("#f3bc00")!, 0),
                new((Color)ColorConverter.ConvertFromString("#ef9500")!, 1)
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

        private void SetRecommendStatus(bool recommendStatus)
        {
            CoreItemBorder.Background = recommendStatus ? _recommendBrush : null;
            if (recommendStatus)
                CoreNameReplacer.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ffffff")!);
        }

        private bool GetRecommendStatus()
        {
            return CoreItemBorder.Background == _recommendBrush;
        }
    }
}