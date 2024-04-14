using System.Windows.Controls;

namespace MCServerLauncher.View.Components
{
    /// <summary>
    /// IconText.xaml 的交互逻辑
    /// </summary>
    public partial class IconText : UserControl
    {
        public IconText()
        {
            InitializeComponent();
        }
        public string Icon
        {
            get => IconView.Glyph;
            set => IconView.Glyph = value;
        }
        public string Text
        {
            get => TextView.Text;
            set => TextView.Text = value;
        }
    }
}
