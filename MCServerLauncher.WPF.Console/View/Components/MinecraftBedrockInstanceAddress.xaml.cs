namespace MCServerLauncher.WPF.Console.View.Components
{
    /// <summary>
    ///     MinecraftBedrockInstanceAddress.xaml 的交互逻辑
    /// </summary>
    public partial class MinecraftBedrockInstanceAddress
    {
        public MinecraftBedrockInstanceAddress()
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