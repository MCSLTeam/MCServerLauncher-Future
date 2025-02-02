using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace MCServerLauncher.WPF.View.Components.ResDownloadItem
{
    /// <summary>
    ///    FastMirrorResCoreItem.xaml 的交互逻辑
    /// </summary>
    public partial class FastMirrorResCoreItem
    {
        /// <summary>
        ///    Recommend brush
        /// </summary>
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

        /// <summary>
        ///    Core name.
        /// </summary>
        public string? CoreName
        {
            get => CoreNameReplacer.Text;
            set => CoreNameReplacer.Text = value;
        }

        /// <summary>
        ///    Tag of core.
        /// </summary>
        public string? CoreTag
        {
            get => CoreTagView.Content.ToString();
            set => CoreTagView.Content = value;
        }

        /// <summary>
        ///    Recommend status.
        /// </summary>
        public bool Recommend
        {
            get => GetRecommendStatus();
            set => SetRecommendStatus(value);
        }

        /// <summary>
        ///    Homepage of core.
        /// </summary>
        public string? HomePage { get; set; }

        /// <summary>
        ///    Support Minecraft versions.
        /// </summary>
        public List<string?>? MinecraftVersions { get; set; }

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
        //private object ConvertColor(object value)
        //{
        //    if (value is SolidColorBrush brush)
        //    {
        //        Color color = brush.Color;
        //        Color invertedColor = Color.FromArgb(color.A, (byte)(255 - color.R), (byte)(255 - color.G), (byte)(255 - color.B));
        //        return new SolidColorBrush(invertedColor);
        //    }
        //    return value;
        //}
    }
}