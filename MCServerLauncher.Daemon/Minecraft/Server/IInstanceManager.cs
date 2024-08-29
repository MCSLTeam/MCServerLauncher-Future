using System.Diagnostics;

namespace MCServerLauncher.Daemon.Minecraft.Server;

public record struct InstanceStatus; // TODO

public interface IInstanceManager
{
    void AddServer(string instanceName, Action<ServerConfig> serverFactory);
    void RemoveServer(string instanceName);

    bool TryStartServer(string instanceName, out Process process);
    bool TryStopServer(string instanceName);

    void SendToServer(string instanceName, string message);

    void KillServer(string instanceName);
    InstanceStatus? GetServerStatus(string instanceName);

    IDictionary<string, InstanceStatus> GetAllStatus();
}