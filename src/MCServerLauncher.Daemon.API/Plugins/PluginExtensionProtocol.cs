using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Nodes;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Common.Contracts.Serialization;
using MCServerLauncher.Daemon.API.Protocol;

namespace MCServerLauncher.Daemon.API.Plugins;

/// <summary>
/// Creates MCSL Extension Protocol envelopes and the plugin-owned push event descriptor.
/// </summary>
public static class PluginExtensionProtocol
{
    public const string Version = "mcsl.extension.v1";
    public const string ExtensionEventRelativeName = "extension";

    private static readonly FrozenSet<string> UnsafeKeys = new[]
    {
        "__proto__",
        "prototype",
        "constructor"
    }.ToFrozenSet(StringComparer.Ordinal);

    public static string CreateExtensionEventName(string pluginId)
    {
        var normalizedPluginId = ProtocolIdentifier.ValidatePluginId(pluginId, nameof(pluginId));
        return $"plugin.{normalizedPluginId}.event.{ExtensionEventRelativeName}";
    }

    public static EventDescriptor<ExtensionProtocolDispatchResult, EmptyRequest> CreateExtensionEvent(
        string pluginId)
    {
        var eventName = CreateExtensionEventName(pluginId);
        return PluginProtocol.CreateEvent<ExtensionProtocolDispatchResult, EmptyRequest>(
            eventName,
            eventName,
            BuiltInProtocolJsonContext.Default.ExtensionProtocolDispatchResult,
            null,
            new EventDocumentation(
                "extensions",
                "Plugin extension protocol event",
                "Publishes one MCSL Extension Protocol envelope from daemon plugin business logic to client extension runtimes.",
                "ExtensionProtocolDispatchResult",
                null));
    }

    public static ExtensionProtocolDispatchResult StatePatch(
        string pluginId,
        long revision,
        JsonElement patch)
    {
        var normalizedPluginId = ProtocolIdentifier.ValidatePluginId(pluginId, nameof(pluginId));
        EnsureRevision(revision);
        var patchNode = CloneObjectNode(patch, "patch");
        var envelope = BaseEnvelope("state.patch", normalizedPluginId);
        envelope["revision"] = revision;
        envelope["patch"] = patchNode;
        return ToDispatchResult(envelope);
    }

    public static ExtensionProtocolDispatchResult StateSnapshot(
        string pluginId,
        long revision,
        JsonElement state)
    {
        var normalizedPluginId = ProtocolIdentifier.ValidatePluginId(pluginId, nameof(pluginId));
        EnsureRevision(revision);
        var stateNode = CloneObjectNode(state, "state");
        var envelope = BaseEnvelope("state.snapshot", normalizedPluginId);
        envelope["revision"] = revision;
        envelope["state"] = stateNode;
        return ToDispatchResult(envelope);
    }

    public static ExtensionProtocolDispatchResult Event(
        string pluginId,
        string name,
        int version,
        JsonElement data,
        JsonElement? meta = null)
    {
        var normalizedPluginId = ProtocolIdentifier.ValidatePluginId(pluginId, nameof(pluginId));
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (version < 1)
            throw new ArgumentOutOfRangeException(nameof(version), "Extension event version must be positive.");

        var dataNode = CloneObjectNode(data, "data");
        var envelope = BaseEnvelope("event", normalizedPluginId);
        envelope["name"] = name;
        envelope["version"] = version;
        envelope["data"] = dataNode;
        if (meta is { } metaElement)
            envelope["meta"] = CloneObjectNode(metaElement, "meta");
        return ToDispatchResult(envelope);
    }

    private static JsonObject BaseEnvelope(string type, string pluginId) => new()
    {
        ["protocol"] = Version,
        ["type"] = type,
        ["plugin"] = pluginId
    };

    private static JsonObject CloneObjectNode(JsonElement element, string parameterName)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new ArgumentException($"Extension protocol {parameterName} must be a JSON object.", parameterName);

        ValidateJsonValue(element, parameterName);
        var node = JsonNode.Parse(element.GetRawText()) as JsonObject;
        return node ?? throw new ArgumentException($"Extension protocol {parameterName} must be a JSON object.", parameterName);
    }

    private static void ValidateJsonValue(JsonElement element, string path)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (UnsafeKeys.Contains(property.Name))
                        throw new ArgumentException($"Unsafe extension protocol object key at '{path}.{property.Name}'.", path);
                    ValidateJsonValue(property.Value, $"{path}.{property.Name}");
                }
                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var child in element.EnumerateArray())
                    ValidateJsonValue(child, $"{path}[{index++}]");
                break;
            case JsonValueKind.String:
            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
            case JsonValueKind.Null:
                break;
            default:
                throw new ArgumentException($"Extension protocol value at '{path}' must be JSON-serializable.", path);
        }
    }

    private static void EnsureRevision(long revision)
    {
        if (revision < 0)
            throw new ArgumentOutOfRangeException(nameof(revision), "Extension protocol revision must be non-negative.");
    }

    private static ExtensionProtocolDispatchResult ToDispatchResult(JsonObject envelope)
    {
        using var document = JsonDocument.Parse(envelope.ToJsonString());
        return new ExtensionProtocolDispatchResult(document.RootElement.Clone());
    }
}
