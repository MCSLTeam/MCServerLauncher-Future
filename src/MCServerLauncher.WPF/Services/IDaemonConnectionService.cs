using System.Threading.Tasks;
using MCServerLauncher.DaemonClient;
using MCServerLauncher.WPF.Modules;

namespace MCServerLauncher.WPF.Services;

public interface IDaemonConnectionService
{
    Task<IDaemon?> GetAsync(Constants.DaemonConfigModel config);
    Task RemoveAsync(Constants.DaemonConfigModel config);
}
