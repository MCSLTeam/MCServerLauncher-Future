using MCServerLauncher.Common.ProtoType.Instance;

namespace MCServerLauncher.Daemon.Minecraft.Server.Factory;

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
public class InstanceFactoryAttribute : Attribute
{
    public readonly InstanceType InstanceType;
    public readonly string MaxVersion;
    public readonly string MinVersion;

    public InstanceFactoryAttribute(InstanceType instanceType, string minVersion = "0.0.0",
        string maxVersion = "255.255.255"
    )
    {
        InstanceType = instanceType;
        MinVersion = minVersion;
        MaxVersion = maxVersion;
    }
}