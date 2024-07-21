using System.Windows.Controls;

namespace MCServerLauncher.WPF.Main.View.Components
{
    /// <summary>
    /// MSLAPIResCoreVersionItem.xaml 的交互逻辑
    /// </summary>
    public partial class MSLAPIResCoreVersionItem : UserControl
    {
        public MSLAPIResCoreVersionItem()
        {
            InitializeComponent();
        }
        public string MinecraftVersion
        {
            get => MinecraftVersionReplacer.Text;
            set => MinecraftVersionReplacer.Text = value;
        }
    }
}
