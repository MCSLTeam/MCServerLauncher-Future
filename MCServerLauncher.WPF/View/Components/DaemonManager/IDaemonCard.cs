using System.Threading.Tasks;

namespace MCServerLauncher.WPF.View.Components.DaemonManager
{
    internal interface IDaemonCard
    {
        string Address { get; set; }
        string Status { get; set; }
        bool IsSecure { get; set; }
        string EndPoint { get; set; }
        int Port { get; set; }
        string Username { get; set; }
        string Password { get; set; }
        string FriendlyName { get; set; }
        Task ConnectDaemon();
    }
}
