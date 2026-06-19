using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace MCServerLauncher.Common.Internal.Performance;

/// <summary>
/// Internal typed JsonElement helpers for daemon/common hot paths.
/// Keeps the public JsonElement contract while avoiding repeated resolver lookup at call sites.
/// </summary>
internal static class JsonElementHotPathAdapters
{
    public static T Deserialize<T>(string json, JsonTypeInfo<T> typeInfo)
    {
        return JsonSerializer.Deserialize(json, typeInfo)!;
    }

    public static T Deserialize<T>(JsonElement element, JsonTypeInfo<T> typeInfo)
    {
        return JsonSerializer.Deserialize(element, typeInfo)!;
    }

    public static JsonElement SerializeToElement<T>(T value, JsonTypeInfo<T> typeInfo)
    {
        return JsonSerializer.SerializeToElement(value, typeInfo);
    }
}
