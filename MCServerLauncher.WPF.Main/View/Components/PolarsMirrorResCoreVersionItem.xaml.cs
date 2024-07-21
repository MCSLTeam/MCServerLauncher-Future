using System.Windows.Controls;

namespace MCServerLauncher.WPF.Main.View.Components
{
    /// <summary>
    /// PolarsMirrorResCoreVersionItem.xaml 的交互逻辑
    /// </summary>
    public partial class PolarsMirrorResCoreVersionItem : UserControl
    {
        public PolarsMirrorResCoreVersionItem()
        {
            InitializeComponent();
        }
        public string FileName
        {
            get => FileNameReplacer.Text;
            set => FileNameReplacer.Text = value;
        }
    }
}
