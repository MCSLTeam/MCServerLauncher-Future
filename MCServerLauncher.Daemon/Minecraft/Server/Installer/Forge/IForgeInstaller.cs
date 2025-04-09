using MCServerLauncher.Daemon.Minecraft.Server.Installer.Forge.Json;

namespace MCServerLauncher.Daemon.Minecraft.Server.Installer.Forge;

public interface IForgeInstaller
{
    public InstallV1 Install { get; }
}