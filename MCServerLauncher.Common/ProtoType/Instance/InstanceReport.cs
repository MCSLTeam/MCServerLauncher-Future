namespace MCServerLauncher.Common.ProtoType.Instance;

public record InstanceReport(InstanceStatus Status, InstanceConfig Config, string[] Properties, Player[] Players);