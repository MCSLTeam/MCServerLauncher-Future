using System.Windows.Controls;

namespace MCServerLauncher.WPF.Console.View.Components
{
    /// <summary>
    /// PlayerItem.xaml 的交互逻辑
    /// </summary>
    public partial class PlayerItem : UserControl
    {
        public PlayerItem()
        {
            InitializeComponent();
        }
        public string PlayerName
        {
            get => PlayerNameTextBlock.Text;
            set => PlayerNameTextBlock.Text = value;
        }
        public string PlayerIP
        {
            get => IPTextBox.Text;
            set => IPTextBox.Text = value;
        }
    }
}
