using MCServerLauncher.Common.ProtoType.Instance;

namespace MCServerLauncher.Daemon.API.State;

/// <summary>
/// Immutable public facts for a managed instance.
/// </summary>
public sealed record InstanceSnapshot(
    Guid Id,
    string Name,
    InstanceType InstanceType,
    string Version,
    InstanceStatus Status);
