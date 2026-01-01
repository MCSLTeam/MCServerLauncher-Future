using MCServerLauncher.WPF.InstanceConsole.Modules;
using Serilog;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace MCServerLauncher.WPF.InstanceConsole.View.Components
{
    /// <summary>
    ///    MinecraftInstancePlayerList.xaml 的交互逻辑
    /// </summary>
    public partial class MinecraftInstancePlayerList : IInstanceBoardComponent
    {
        private bool _isLoading;
        private bool _hasError;

        public MinecraftInstancePlayerList()
        {
            InitializeComponent();
        }

        public bool IsLoading
        {
            get => _isLoading;
            private set => _isLoading = value;
        }

        public bool HasError
        {
            get => _hasError;
            private set => _hasError = value;
        }

        public async Task InitializeAsync()
        {
            try
            {
                IsLoading = true;
                HasError = false;

                // Subscribe to report updates
                InstanceDataManager.Instance.ReportUpdated += OnReportUpdated;

                await RefreshAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[MinecraftInstancePlayerList] Failed to initialize");
                HasError = true;
            }
            finally
            {
                IsLoading = false;
            }
        }

        public async Task RefreshAsync()
        {
            try
            {
                var report = InstanceDataManager.Instance.CurrentReport;
                if (report?.Players != null)
                {
                    Dispatcher.Invoke(() =>
                    {
                        PlayerListView.Items.Clear();
                        foreach (var player in report.Players)
                        {
                            PlayerListView.Items.Add(new PlayerItem 
                            { 
                                PlayerName = player.Name,
                                PlayerIP = player.Uuid.ToString() // UUID as identifier
                            });
                        }
                    });
                }

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[MinecraftInstancePlayerList] Failed to refresh");
                HasError = true;
            }
        }

        private async void OnReportUpdated(object? sender, Common.ProtoType.Instance.InstanceReport? report)
        {
            await RefreshAsync();
        }

        /// <summary>
        ///    Online players list (legacy setter for compatibility).
        /// </summary>
        public string PlayerList
        {
            get => string.Join(",", PlayerListView.Items.Cast<PlayerItem>().Select(p => $"{p.PlayerName}@{p.PlayerIP}"));
            set
            {
                Dispatcher.Invoke(() =>
                {
                    PlayerListView.Items.Clear();
                    var currentPlayers = value.Split(',');
                    foreach (var player in currentPlayers)
                    {
                        var playerInfo = player.Split('@');
                        if (playerInfo.Length >= 2)
                        {
                            PlayerListView.Items.Add(new PlayerItem 
                            { 
                                PlayerName = playerInfo[0], 
                                PlayerIP = playerInfo[1] 
                            });
                        }
                    }
                });
            }
        }
    }
}