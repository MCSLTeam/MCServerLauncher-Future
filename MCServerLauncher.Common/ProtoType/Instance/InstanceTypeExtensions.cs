using System.Collections.Concurrent;
using System.Reflection;

namespace MCServerLauncher.Common.ProtoType.Instance;

public static class InstanceTypeExtensions
{
    private static readonly ConcurrentDictionary<InstanceType, InstanceTypeMetadataAttribute> MetadataCache = new();

    private static InstanceTypeMetadataAttribute GetMetadata(this InstanceType type)
    {
        return MetadataCache.GetOrAdd(type, t =>
        {
            var field = typeof(InstanceType).GetField(t.ToString());
            return field?.GetCustomAttribute<InstanceTypeMetadataAttribute>()
                   ?? new InstanceTypeMetadataAttribute();
        });
    }

    public static InstanceCategory GetCategory(this InstanceType type) => type.GetMetadata().Category;

    public static bool IsMinecraftJavaRuntimeType(this InstanceType type) =>
        type.GetMetadata().Category.HasFlag(InstanceCategory.MinecraftJava)
        && !type.GetMetadata().Category.HasFlag(InstanceCategory.Proxy)
        && !type.GetMetadata().Category.HasFlag(InstanceCategory.Utility);

    public static bool SupportsMinecraftBoardWidgets(this InstanceType type) =>
        type.GetMetadata().SupportsMinecraftBoardWidgets;

    public static bool RequiresNumericMinecraftVersion(this InstanceType type) =>
        type.GetMetadata().RequiresNumericVersion;

    public static bool IsMinecraftLikeType(this InstanceType type) =>
        type.GetMetadata().Category.HasFlag(InstanceCategory.MinecraftJava)
        || type.GetMetadata().Category.HasFlag(InstanceCategory.MinecraftBedrock);

    public static bool IsGenericFallbackType(this InstanceType type) =>
        type.GetMetadata().IsGenericFallback;
}
