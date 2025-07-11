using MCServerLauncher.WPF.Modules;
using System.Windows;

namespace MCServerLauncher.WPF.InstanceConsole.View.Components
{
    /// <summary>
    ///    PlayerItem.xaml 的交互逻辑
    /// </summary>
    public partial class PlayerItem
    {
        public PlayerItem()
        {
            InitializeComponent();
        }

        /// <summary>
        ///    Player name.
        /// </summary>
        public string PlayerName
        {
            get => PlayerNameTextBlock.Text;
            set => PlayerNameTextBlock.Text = value;
        }

        /// <summary>
        ///    Player login IP address.
        /// </summary>
        public string PlayerIP
        {
            get => IPTextBox.Text;
            set => IPTextBox.Text = value;
        }
        private void ToggleIP(object sender, RoutedEventArgs e)
        {
            IPTextBox.Visibility = IPTextBox.Visibility == Visibility.Hidden ? Visibility.Visible : Visibility.Hidden;
            ToggleIPButton.Content = IPTextBox.Visibility == Visibility.Hidden ? Lang.Tr["ViewIPAddress"] : Lang.Tr["ClickToHide"];
        }
    }
}