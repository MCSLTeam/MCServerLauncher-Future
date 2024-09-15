namespace MCServerLauncher.WPF.InstanceConsole.View.Components
{
    /// <summary>
    ///    MinecraftInstanceAddress.xaml 的交互逻辑
    /// </summary>
    public partial class MinecraftInstanceAddress
    {
        public MinecraftInstanceAddress()
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