using MCServerLauncher.Common.ProtoType.Instance;

namespace MCServerLauncher.Daemon.Minecraft.Server;

public static class InstanceStatusExtensions
{
    public static bool IsStoppedOrCrashed(this InstanceStatus status)
    {
        return status is InstanceStatus.Stopped or InstanceStatus.Crashed;
    }
}