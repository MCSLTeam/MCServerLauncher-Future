using MCServerLauncher.WPF.Modules;
using MCServerLauncher.WPF.View.Components.DaemonManager;

namespace MCServerLauncher.WPF.View.Pages
{
    /// <summary>
    ///    DaemonManagerPage.xaml 的交互逻辑
    /// </summary>
    public partial class DaemonManagerPage
    {
        public DaemonManagerPage()
        {
            InitializeComponent();
            // Refresh trigger when page is visible
            IsVisibleChanged += (s, e) =>
            {
                DaemonCardContainer.Items.Clear();
                foreach (DaemonsListManager.DaemonConfigModel daemon in DaemonsListManager.DaemonList)
                {
                    DaemonCard daemonCard = new DaemonCard
                    {
                        Address = $"{(daemon.IsSecure ? "wss" : "ws")}://{daemon.EndPoint}:{daemon.Port}",
                        IsSecure = daemon.IsSecure,
                        EndPoint = daemon.EndPoint,
                        Port = daemon.Port,
                        Username = daemon.Username,
                        Password = daemon.Password,
                        FriendlyName = daemon.FriendlyName ?? LanguageManager.Localize["Main_DaemonManagerNavMenu"],
                    };
                    DaemonCardContainer.Items.Add(daemonCard);
                    daemonCard.ConnectDaemon();
                }
            };
        }
    }
}