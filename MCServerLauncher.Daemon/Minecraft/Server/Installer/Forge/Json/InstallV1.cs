namespace MCServerLauncher.Daemon.Minecraft.Server.Installer.Forge.Json;

public class InstallV1 : Install
{
    public string ServerJarPath { get; set; } = "{ROOT}/minecraft_server.{MINECRAFT_VERSION}.jar";
}