using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Common.Contracts.Serialization;
using MCServerLauncher.Daemon.API.Events;
using MCServerLauncher.Daemon.API.Plugins;
using MCServerLauncher.Daemon.API.Protocol;

namespace MCServerLauncher.WPF.Features.Plugins;

internal static class PluginExtensionDaemonEventBridge
{
    internal static string? ToDaemonEventName(string eventPermission) => eventPermission switch
    {
        "daemon.instance.catalog.changed" => "mcsl.event.instance.catalog.changed",
        "daemon.instance.log" => "mcsl.event.instance.log",
        "daemon.report" => "mcsl.event.daemon.report",
        "daemon.notification" => "mcsl.event.notification",
        _ when IsPluginExtensionEventName(eventPermission) => eventPermission,
        _ => null
    };

    internal static bool IsPluginExtensionEventName(string eventName) =>
        eventName.StartsWith("plugin.", StringComparison.Ordinal) &&
        eventName.EndsWith(".event.extension", StringComparison.Ordinal);

    internal static string? ToExtensionEventName(string daemonEventName) => daemonEventName switch
    {
        "mcsl.event.instance.catalog.changed" => "daemon.instance.catalog.changed",
        "mcsl.event.instance.log" => "daemon.instance.log",
        "mcsl.event.daemon.report" => "daemon.report",
        "mcsl.event.notification" => "daemon.notification",
        _ => null
    };

    internal static PluginExtensionProtocolParseResult CreateBuiltInEventEnvelope<TData, TMeta>(
        PluginClientExtensionCacheEntry entry,
        string extensionEventName,
        DaemonEvent<TData, TMeta> daemonEvent)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionEventName);
        ArgumentNullException.ThrowIfNull(daemonEvent);

        if (!entry.EventPermissions.Contains(extensionEventName, StringComparer.Ordinal))
        {
            return PluginExtensionProtocolParseResult.Failure(
            [
                new PluginExtensionProtocolDiagnostic(
                    "event_undeclared",
                    "$.name",
                    $"Event '{extensionEventName}' is not declared by installed extension '{entry.Id}'.")
            ]);
        }

        var data = ToJsonObject(daemonEvent.Data.Kind == DaemonEventFieldKind.Value ? daemonEvent.Data.Value : null);
        var meta = ToJsonObject(daemonEvent.Meta.Kind == DaemonEventFieldKind.Value ? daemonEvent.Meta.Value : null);
        Dictionary<string, object?> envelope = new(StringComparer.Ordinal)
        {
            ["protocol"] = PluginExtensionProtocol.Version,
            ["type"] = "event",
            ["plugin"] = entry.Id,
            ["name"] = extensionEventName,
            ["version"] = 1,
            ["data"] = data
        };
        if (meta.Count > 0)
            envelope["meta"] = meta;

        return PluginExtensionProtocol.Parse(JsonSerializer.Serialize(envelope));
    }

    internal static EventDescriptor<ExtensionProtocolDispatchResult, EmptyRequest> CreatePluginExtensionEventDescriptor(string eventName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        if (!IsPluginExtensionEventName(eventName))
            throw new ArgumentException("Extension protocol plugin events must use plugin.<id>.event.extension.", nameof(eventName));

        return PluginProtocol.CreateEvent<ExtensionProtocolDispatchResult, EmptyRequest>(
            eventName,
            eventName,
            BuiltInProtocolJsonContext.Default.ExtensionProtocolDispatchResult,
            null,
            new EventDocumentation(
                "extensions",
                "Plugin extension protocol event",
                "Publishes one MCSL Extension Protocol envelope from a daemon C# plugin.",
                "ExtensionProtocolDispatchResult",
                null));
    }

    internal static PluginExtensionProtocolParseResult ParsePluginExtensionEvent(
        DaemonEvent<ExtensionProtocolDispatchResult, EmptyRequest> daemonEvent)
    {
        ArgumentNullException.ThrowIfNull(daemonEvent);
        if (daemonEvent.Data.Kind != DaemonEventFieldKind.Value)
        {
            return PluginExtensionProtocolParseResult.Failure(
            [
                new PluginExtensionProtocolDiagnostic("event_data_missing", "$.data", "Plugin extension event data is missing.")
            ]);
        }

        return PluginExtensionProtocol.Parse(daemonEvent.Data.Value.Envelope.GetRawText());
    }

    private static IReadOnlyDictionary<string, object?> ToJsonObject(object? value)
    {
        if (value is null)
            return new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>(StringComparer.Ordinal));

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(value));
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            return new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>(StringComparer.Ordinal));

        return new ReadOnlyDictionary<string, object?>(ReadObject(document.RootElement));
    }

    private static Dictionary<string, object?> ReadObject(JsonElement element) =>
        element.EnumerateObject().ToDictionary(property => property.Name, property => ReadValue(property.Value), StringComparer.Ordinal);

    private static object? ReadValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => ReadObject(element),
        JsonValueKind.Array => element.EnumerateArray().Select(ReadValue).ToArray(),
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number when element.TryGetInt64(out var integer) => integer,
        JsonValueKind.Number when element.TryGetDouble(out var number) => number,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => null
    };
}
