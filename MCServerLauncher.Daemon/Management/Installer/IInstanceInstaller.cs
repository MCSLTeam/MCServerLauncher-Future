using MCServerLauncher.Common.ProtoType.Instance;

namespace MCServerLauncher.Daemon.Management.Installer;

public interface IInstanceInstaller
{
    Task<bool> Run(InstanceFactorySetting setting, CancellationToken ct = default);
}