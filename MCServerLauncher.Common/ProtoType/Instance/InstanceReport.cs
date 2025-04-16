namespace MCServerLauncher.Common.ProtoType.Instance;

public record InstanceReport(InstanceStatus Status, InstanceConfig Config, List<string> Properties, Player[] Players);