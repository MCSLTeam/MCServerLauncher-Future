using MCServerLauncher.Common.ProtoType.Instance;

namespace MCServerLauncher.Daemon.Minecraft.Extensions;

public static class InstanceStatusExtensions
{
    public static bool IsStoppedOrCrashed(this InstanceStatus status)
    {
        return status is InstanceStatus.Stopped or InstanceStatus.Crashed;
    }
}