namespace MCServerLauncher.Common.ProtoType.Instance;

[AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
public sealed class InstanceTypeMetadataAttribute : Attribute
{
    public InstanceCategory Category { get; init; } = InstanceCategory.Generic;
    public bool SupportsMinecraftBoardWidgets { get; init; }
    public bool RequiresNumericVersion { get; init; }
    public bool IsGenericFallback { get; init; }
}
