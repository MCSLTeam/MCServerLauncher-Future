namespace MCServerLauncher.Daemon.Management.Installer.MinecraftForge.Json;

public class InstallV1 : Install
{
    public string ServerJarPath { get; set; } = "{ROOT}/minecraft_server.{MINECRAFT_VERSION}.jar";
}