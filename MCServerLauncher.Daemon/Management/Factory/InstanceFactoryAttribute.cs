using MCServerLauncher.Common.ProtoType.Instance;

namespace MCServerLauncher.Daemon.Management.Factory;

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
public class InstanceFactoryAttribute : Attribute
{
    public readonly SourceType AllowedSourceType;
    public readonly InstanceType InstanceType;
    public readonly string MaxVersion;
    public readonly string MinVersion;

    public InstanceFactoryAttribute(
        InstanceType instanceType,
        SourceType allowedSourceType = SourceType.None,
        string minVersion = "0.0.0",
        string maxVersion = "255.255.255"
    )
    {
        InstanceType = instanceType;
        AllowedSourceType = allowedSourceType;
        MinVersion = minVersion;
        MaxVersion = maxVersion;
    }
}