using MCServerLauncher.Common.ProtoType.Instance;

namespace MCServerLauncher.Daemon.Minecraft.Server;

public static class InstanceStatusExtensions
{
    public static bool IsStoppedOrCrashed(this ServerStatus status)
    {
        return status is ServerStatus.Stopped or ServerStatus.Crashed;
    }
}