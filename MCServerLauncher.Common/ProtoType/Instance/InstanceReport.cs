namespace MCServerLauncher.Common.ProtoType.Instance;

public record InstanceReport(
    InstanceStatus Status,
    InstanceConfig Config,
    Dictionary<string, string> Properties,
    Player[] Players,
    InstancePerformanceCounter PerformanceCounter
);