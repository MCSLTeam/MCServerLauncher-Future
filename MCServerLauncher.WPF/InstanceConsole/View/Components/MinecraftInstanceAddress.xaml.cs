using MCServerLauncher.WPF.Modules;
using System.Windows;

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

        private void ToggleIP(object sender, RoutedEventArgs e)
        {
            AddressTextBox.Visibility = AddressTextBox.Visibility == Visibility.Hidden ? Visibility.Visible : Visibility.Hidden;
            ToggleIPButton.Content = AddressTextBox.Visibility == Visibility.Hidden ? Lang.Tr["ClickToView"] : Lang.Tr["ClickToHide"];
        }
    }
}