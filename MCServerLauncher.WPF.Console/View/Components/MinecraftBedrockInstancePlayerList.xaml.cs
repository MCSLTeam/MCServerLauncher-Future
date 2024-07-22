using System.Windows.Controls;

namespace MCServerLauncher.WPF.Console.View.Components
{
    /// <summary>
    /// MinecraftBedrockInstancePlayerList.xaml 的交互逻辑
    /// </summary>
    public partial class MinecraftBedrockInstancePlayerList : UserControl
    {
        public MinecraftBedrockInstancePlayerList()
        {
            InitializeComponent();
        }
        public string PlayerList
        {
            get => string.Join(",", PlayerListView.Items.ToString());
            set
            {
                PlayerListView.Items.Clear();
                string[] CurrentPlayers = value.Split(',');
                foreach (string Player in CurrentPlayers)
                {
                    string[] PlayerInfo = Player.Split('@');
                    PlayerListView.Items.Add(new PlayerItem() { PlayerName = PlayerInfo[0], PlayerIP = PlayerInfo[1] });
                }
            }
        }
    }
}
