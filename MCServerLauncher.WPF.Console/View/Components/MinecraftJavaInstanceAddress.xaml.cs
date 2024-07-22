using System.Windows.Controls;

namespace MCServerLauncher.WPF.Console.View.Components
{
    /// <summary>
    /// MinecraftJavaInstanceAddress.xaml 的交互逻辑
    /// </summary>
    public partial class MinecraftJavaInstanceAddress : UserControl
    {
        public MinecraftJavaInstanceAddress()
        {
            InitializeComponent();
        }
        public string ServerIP
        {
            get => AddressTextBox.Text;
            set => AddressTextBox.Text = value;
        }
    }
}
