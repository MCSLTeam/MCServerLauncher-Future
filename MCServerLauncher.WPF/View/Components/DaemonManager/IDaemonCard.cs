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
        string Token { get; set; }
        string FriendlyName { get; set; }
    }
}
