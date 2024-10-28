namespace MCServerLauncher.Daemon.Minecraft.Server.Factory;

public class InstanceFactorySetting : InstanceConfig
{
    // TODO 支持网络上的Source
    public string Source { get; set; }
    public SourceType SourceType { get; set; }

    public InstanceConfig GetInstanceConfig()
    {
        return this;
    }
}

public enum SourceType
{
    Archive,
    Core,
    Script
}