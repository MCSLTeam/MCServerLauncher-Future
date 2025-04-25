namespace MCServerLauncher.Daemon.Minecraft.Server.Factory;

[InstanceFactory(InstanceType.Fabric)]
public class FabricFactory : ICoreInstanceFactory
{
    public Task<InstanceConfig> CreateInstanceFromCore(InstanceFactorySetting setting)
    {
        throw new NotImplementedException();
    }
}