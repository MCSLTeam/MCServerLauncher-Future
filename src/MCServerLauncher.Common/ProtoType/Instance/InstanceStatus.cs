namespace MCServerLauncher.Common.ProtoType.Instance;

public enum InstanceStatus
{
    // Numeric values preserved for any integer-persisted snapshots.
    Running = 0,
    Stopped = 1,
    Crashed = 2,
    Starting = 3,
    Stopping = 4,
}
