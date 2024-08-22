namespace MCServerLauncher.WPF.Console.View.Components
{
    /// <summary>
    ///    MinecraftJavaInstanceAddress.xaml 的交互逻辑
    /// </summary>
    public partial class MinecraftJavaInstanceAddress
    {
        public MinecraftJavaInstanceAddress()
        {
            InitializeComponent();
        }

        /// <summary>
        ///    Server IP address.
        /// </summary>
        public string ServerIP
        {
            get => AddressTextBox.Text;
            set => AddressTextBox.Text = value;
        }
    }
}