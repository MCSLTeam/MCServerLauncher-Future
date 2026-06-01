using System.Threading.Tasks;
using MCServerLauncher.DaemonClient;
using MCServerLauncher.WPF.Modules;

namespace MCServerLauncher.WPF.Services;

public class DaemonConnectionService : IDaemonConnectionService
{
    public Task<IDaemon?> GetAsync(Constants.DaemonConfigModel config)
    {
        return DaemonsWsManager.Get(config);
    }

    public Task RemoveAsync(Constants.DaemonConfigModel config)
    {
        return DaemonsWsManager.Remove(config);
    }
}
