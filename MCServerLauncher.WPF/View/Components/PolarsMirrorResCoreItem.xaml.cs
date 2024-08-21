using System.Collections.Generic;

namespace MCServerLauncher.WPF.View.Components
{
    /// <summary>
    ///     PolarsMirrorResCoreItem.xaml 的交互逻辑
    /// </summary>
    public partial class PolarsMirrorResCoreItem
    {
        public PolarsMirrorResCoreItem()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Core name.
        /// </summary>
        public string CoreName
        {
            get => CoreNameReplacer.Text;
            set => CoreNameReplacer.Text = value;
        }

        /// <summary>
        /// Description of the core.
        /// </summary>
        public string CoreDescription { get; set; }

        /// <summary>
        /// Index of the core.
        /// </summary>
        public int CoreId { get; set; }

        /// <summary>
        /// Raw icon URL of the core.
        /// </summary>
        public string CoreIconUrl { get; set; }

        /// <summary>
        /// Minecraft versions supported by the core.
        /// </summary>
        public List<string> MinecraftVersions { get; set; }
    }
}