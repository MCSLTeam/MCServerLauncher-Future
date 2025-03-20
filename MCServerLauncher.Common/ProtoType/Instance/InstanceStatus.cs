namespace MCServerLauncher.Common.ProtoType.Instance;

public record InstanceStatus(ServerStatus Status, InstanceConfig Config, List<string> Properties, List<string> Players);