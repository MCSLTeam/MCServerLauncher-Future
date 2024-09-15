using System.Diagnostics;

namespace MCServerLauncher.Daemon.Minecraft.Server;

public interface IInstanceManager
{
    bool TryAddServer(string instanceName, Action<ServerConfig> serverFactory);
    bool TryRemoveServer(string instanceName);

    bool TryStartServer(string instanceName, out Process? process);
    bool TryStopServer(string instanceName);

    void SendToServer(string instanceName, string message);

    void KillServer(string instanceName);
    InstanceStatus GetServerStatus(string instanceName);

    IDictionary<string, InstanceStatus> GetAllStatus();
}