namespace MCServerLauncher.WPF.InstanceConsole.View.Components
{
    /// <summary>
    ///    MinecraftInstancePlayerList.xaml 的交互逻辑
    /// </summary>
    public partial class MinecraftInstancePlayerList
    {
        public MinecraftInstancePlayerList()
        {
            InitializeComponent();
        }

        /// <summary>
        ///    Online players list.
        /// </summary>
        public string PlayerList
        {
            get => string.Join(",", PlayerListView.Items.ToString());
            set
            {
                PlayerListView.Items.Clear();
                var currentPlayers = value.Split(',');
                foreach (var player in currentPlayers)
                {
                    var playerInfo = player.Split('@');
                    PlayerListView.Items.Add(new PlayerItem { PlayerName = playerInfo[0], PlayerIP = playerInfo[1] });
                }
            }
        }
    }
}