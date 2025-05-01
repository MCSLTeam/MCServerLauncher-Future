using MCServerLauncher.Common.ProtoType.Instance;

namespace MCServerLauncher.Daemon.Management.Factory;

public class InstanceFactoryException : Exception
{
    public readonly InstanceFactorySetting Setting;

    public InstanceFactoryException(InstanceFactorySetting setting, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Setting = setting;
    }
}