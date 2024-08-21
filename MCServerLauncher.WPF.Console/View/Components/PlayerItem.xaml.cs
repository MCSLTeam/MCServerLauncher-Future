namespace MCServerLauncher.WPF.Console.View.Components
{
    /// <summary>
    ///     PlayerItem.xaml 的交互逻辑
    /// </summary>
    public partial class PlayerItem
    {
        public PlayerItem()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Player name.
        /// </summary>
        public string PlayerName
        {
            get => PlayerNameTextBlock.Text;
            set => PlayerNameTextBlock.Text = value;
        }

        /// <summary>
        /// Player login IP address.
        /// </summary>
        public string PlayerIP
        {
            get => IPTextBox.Text;
            set => IPTextBox.Text = value;
        }
    }
}