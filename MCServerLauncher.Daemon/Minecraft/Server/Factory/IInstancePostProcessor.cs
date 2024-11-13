namespace MCServerLauncher.Daemon.Minecraft.Server.Factory;

public interface IInstancePostProcessor
{
    Task PostProcess(Instance instance);
}