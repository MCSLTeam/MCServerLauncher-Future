using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;

namespace MCServerLauncher.WPF.Features.Plugins;

public sealed record PluginExtensionProtocolDiagnostic(string Code, string Path, string Message);

public sealed record PluginExtensionStatePatchEnvelope(
    string Protocol,
    string Type,
    string Plugin,
    long Revision,
    IReadOnlyDictionary<string, object?> Patch);

public sealed record PluginExtensionStateSnapshotEnvelope(
    string Protocol,
    string Type,
    string Plugin,
    long Revision,
    IReadOnlyDictionary<string, object?> State);

public sealed record PluginExtensionEventEnvelope(
    string Protocol,
    string Type,
    string Plugin,
    string Name,
    int Version,
    IReadOnlyDictionary<string, object?> Data,
    IReadOnlyDictionary<string, object?>? Meta);

public sealed record PluginExtensionErrorEnvelope(
    string Protocol,
    string Type,
    string? Plugin,
    string Code,
    string Message,
    IReadOnlyDictionary<string, object?>? Details);

public sealed record PluginExtensionResponseEnvelope(
    string Protocol,
    string Type,
    string Id,
    string Plugin,
    object? Result,
    PluginExtensionErrorEnvelope? Error);

public sealed record PluginExtensionStateSnapshot(
    string Plugin,
    long Revision,
    IReadOnlyDictionary<string, object?> State);

public sealed record PluginExtensionStatePatchResult(
    bool Applied,
    PluginExtensionStateSnapshot Snapshot);

public sealed record PluginExtensionProtocolParseResult(
    bool Ok,
    PluginExtensionStatePatchEnvelope? StatePatch,
    PluginExtensionStateSnapshotEnvelope? StateSnapshot,
    PluginExtensionEventEnvelope? Event,
    PluginExtensionErrorEnvelope? Error,
    PluginExtensionResponseEnvelope? Response,
    IReadOnlyList<PluginExtensionProtocolDiagnostic> Diagnostics)
{
    public static PluginExtensionProtocolParseResult StatePatchEnvelope(PluginExtensionStatePatchEnvelope envelope) =>
        new(true, envelope, null, null, null, null, []);

    public static PluginExtensionProtocolParseResult StateSnapshotEnvelope(PluginExtensionStateSnapshotEnvelope envelope) =>
        new(true, null, envelope, null, null, null, []);

    public static PluginExtensionProtocolParseResult EventEnvelope(PluginExtensionEventEnvelope envelope) =>
        new(true, null, null, envelope, null, null, []);

    public static PluginExtensionProtocolParseResult ErrorEnvelope(PluginExtensionErrorEnvelope envelope) =>
        new(true, null, null, null, envelope, null, []);

    public static PluginExtensionProtocolParseResult ResponseEnvelope(PluginExtensionResponseEnvelope envelope) =>
        new(true, null, null, null, null, envelope, []);

    public static PluginExtensionProtocolParseResult Failure(IReadOnlyList<PluginExtensionProtocolDiagnostic> diagnostics) =>
        new(false, null, null, null, null, null, diagnostics);
}

public static class PluginExtensionProtocol
{
    public const string Version = "mcsl.extension.v1";

    private static readonly IReadOnlySet<string> UnsafeKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        "__proto__",
        "prototype",
        "constructor"
    };

    public static JsonElement CreateRequestEnvelope(
        string requestId,
        string plugin,
        string command,
        IReadOnlyDictionary<string, object?>? parameters = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        ArgumentException.ThrowIfNullOrWhiteSpace(plugin);
        ArgumentException.ThrowIfNullOrWhiteSpace(command);

        Dictionary<string, object?> envelope = new(StringComparer.Ordinal)
        {
            ["protocol"] = Version,
            ["type"] = "request",
            ["id"] = requestId,
            ["plugin"] = plugin,
            ["command"] = command
        };
        if (parameters is not null)
            envelope["params"] = parameters;

        return JsonSerializer.SerializeToElement(envelope);
    }

    public static PluginExtensionProtocolParseResult Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        List<PluginExtensionProtocolDiagnostic> diagnostics = [];
        using JsonDocument document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            return PluginExtensionProtocolParseResult.Failure([Diagnostic("envelope_invalid", "$", "Envelope must be an object.")]);

        var protocol = ReadString(root, "protocol");
        var type = ReadString(root, "type");
        if (protocol != Version)
            diagnostics.Add(Diagnostic("protocol_invalid", "$.protocol", "protocol must be mcsl.extension.v1."));
        if (string.IsNullOrWhiteSpace(type))
            diagnostics.Add(Diagnostic("type_missing", "$.type", "type is required."));

        if (type == "state.patch")
        {
            var plugin = ReadString(root, "plugin");
            if (string.IsNullOrWhiteSpace(plugin))
                diagnostics.Add(Diagnostic("plugin_missing", "$.plugin", "plugin is required."));
            if (!root.TryGetProperty("revision", out var revisionElement) || !revisionElement.TryGetInt64(out var revision) || revision < 0)
                diagnostics.Add(Diagnostic("revision_invalid", "$.revision", "revision must be a non-negative integer."));
            if (!root.TryGetProperty("patch", out var patchElement) || patchElement.ValueKind != JsonValueKind.Object)
            {
                diagnostics.Add(Diagnostic("patch_invalid", "$.patch", "patch must be an object."));
            }
            else
            {
                ValidateJsonObject(patchElement, "$.patch", diagnostics);
            }

            if (diagnostics.Count > 0)
                return PluginExtensionProtocolParseResult.Failure(diagnostics);

            return PluginExtensionProtocolParseResult.StatePatchEnvelope(
                new PluginExtensionStatePatchEnvelope(
                    Version,
                    type,
                    plugin!,
                    revisionElement.GetInt64(),
                    new ReadOnlyDictionary<string, object?>(ReadObject(patchElement))));
        }

        if (type == "state.snapshot")
        {
            var plugin = ReadString(root, "plugin");
            if (string.IsNullOrWhiteSpace(plugin))
                diagnostics.Add(Diagnostic("plugin_missing", "$.plugin", "plugin is required."));
            if (!root.TryGetProperty("revision", out var revisionElement) || !revisionElement.TryGetInt64(out var revision) || revision < 0)
                diagnostics.Add(Diagnostic("revision_invalid", "$.revision", "revision must be a non-negative integer."));
            if (!root.TryGetProperty("state", out var stateElement) || stateElement.ValueKind != JsonValueKind.Object)
            {
                diagnostics.Add(Diagnostic("state_invalid", "$.state", "state must be an object."));
            }
            else
            {
                ValidateJsonObject(stateElement, "$.state", diagnostics);
            }

            if (diagnostics.Count > 0)
                return PluginExtensionProtocolParseResult.Failure(diagnostics);

            return PluginExtensionProtocolParseResult.StateSnapshotEnvelope(
                new PluginExtensionStateSnapshotEnvelope(
                    Version,
                    type,
                    plugin!,
                    revisionElement.GetInt64(),
                    new ReadOnlyDictionary<string, object?>(ReadObject(stateElement))));
        }

        if (type == "event")
        {
            var plugin = ReadString(root, "plugin");
            var name = ReadString(root, "name");
            if (string.IsNullOrWhiteSpace(plugin))
                diagnostics.Add(Diagnostic("plugin_missing", "$.plugin", "plugin is required."));
            if (string.IsNullOrWhiteSpace(name))
                diagnostics.Add(Diagnostic("event_name_missing", "$.name", "event name is required."));
            if (!root.TryGetProperty("version", out var versionElement) || !versionElement.TryGetInt32(out var eventVersion) || eventVersion < 1)
                diagnostics.Add(Diagnostic("event_version_invalid", "$.version", "event version must be a positive integer."));
            if (!root.TryGetProperty("data", out var dataElement) || dataElement.ValueKind != JsonValueKind.Object)
            {
                diagnostics.Add(Diagnostic("event_data_invalid", "$.data", "event data must be an object."));
            }
            else
            {
                ValidateJsonObject(dataElement, "$.data", diagnostics);
            }

            IReadOnlyDictionary<string, object?>? meta = null;
            if (root.TryGetProperty("meta", out var metaElement))
            {
                if (metaElement.ValueKind != JsonValueKind.Object)
                {
                    diagnostics.Add(Diagnostic("event_meta_invalid", "$.meta", "event meta must be an object."));
                }
                else
                {
                    ValidateJsonObject(metaElement, "$.meta", diagnostics);
                    meta = new ReadOnlyDictionary<string, object?>(ReadObject(metaElement));
                }
            }

            if (diagnostics.Count > 0)
                return PluginExtensionProtocolParseResult.Failure(diagnostics);

            return PluginExtensionProtocolParseResult.EventEnvelope(
                new PluginExtensionEventEnvelope(
                    Version,
                    type,
                    plugin!,
                    name!,
                    versionElement.GetInt32(),
                    new ReadOnlyDictionary<string, object?>(ReadObject(dataElement)),
                    meta));
        }

        if (type == "error")
        {
            var code = ReadString(root, "code");
            var message = ReadString(root, "message");
            if (string.IsNullOrWhiteSpace(code))
                diagnostics.Add(Diagnostic("error_code_missing", "$.code", "error code is required."));
            if (string.IsNullOrWhiteSpace(message))
                diagnostics.Add(Diagnostic("error_message_missing", "$.message", "error message is required."));

            Dictionary<string, object?>? details = null;
            if (root.TryGetProperty("details", out var detailsElement))
            {
                if (detailsElement.ValueKind != JsonValueKind.Object)
                {
                    diagnostics.Add(Diagnostic("error_details_invalid", "$.details", "details must be an object."));
                }
                else
                {
                    ValidateJsonObject(detailsElement, "$.details", diagnostics);
                    details = ReadObject(detailsElement);
                }
            }

            if (diagnostics.Count > 0)
                return PluginExtensionProtocolParseResult.Failure(diagnostics);

            return PluginExtensionProtocolParseResult.ErrorEnvelope(
                new PluginExtensionErrorEnvelope(
                    Version,
                    type,
                    ReadString(root, "plugin"),
                    code!,
                    message!,
                    details is null ? null : new ReadOnlyDictionary<string, object?>(details)));
        }

        if (type == "response")
        {
            var id = ReadString(root, "id");
            var plugin = ReadString(root, "plugin");
            if (string.IsNullOrWhiteSpace(id))
                diagnostics.Add(Diagnostic("response_id_missing", "$.id", "response id is required."));
            if (string.IsNullOrWhiteSpace(plugin))
                diagnostics.Add(Diagnostic("plugin_missing", "$.plugin", "plugin is required."));

            object? result = null;
            if (root.TryGetProperty("result", out var resultElement))
            {
                ValidateJsonValue(resultElement, "$.result", diagnostics);
                result = ReadValue(resultElement);
            }

            PluginExtensionErrorEnvelope? error = null;
            if (root.TryGetProperty("error", out var errorElement))
            {
                if (errorElement.ValueKind != JsonValueKind.Object)
                {
                    diagnostics.Add(Diagnostic("response_error_invalid", "$.error", "response error must be an object."));
                }
                else
                {
                    var parsedError = Parse(errorElement.GetRawText());
                    if (!parsedError.Ok || parsedError.Error is null)
                        diagnostics.AddRange(parsedError.Diagnostics.Select(diagnostic => Diagnostic(diagnostic.Code, $"$.error{diagnostic.Path.TrimStart('$')}", diagnostic.Message)));
                    else
                        error = parsedError.Error;
                }
            }

            if (diagnostics.Count > 0)
                return PluginExtensionProtocolParseResult.Failure(diagnostics);

            return PluginExtensionProtocolParseResult.ResponseEnvelope(
                new PluginExtensionResponseEnvelope(
                    Version,
                    type,
                    id!,
                    plugin!,
                    result,
                    error));
        }

        diagnostics.Add(Diagnostic("type_unsupported", "$.type", "Envelope type is not supported by this parser."));
        return PluginExtensionProtocolParseResult.Failure(diagnostics);
    }

    public static PluginExtensionStatePatchResult ApplyStatePatch(
        PluginExtensionStateSnapshot snapshot,
        PluginExtensionStatePatchEnvelope patch)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(patch);

        if (!string.Equals(snapshot.Plugin, patch.Plugin, StringComparison.Ordinal) || patch.Revision <= snapshot.Revision)
            return new PluginExtensionStatePatchResult(false, snapshot);

        Dictionary<string, object?> next = new(snapshot.State, StringComparer.Ordinal);
        foreach (var item in patch.Patch)
            next[item.Key] = item.Value;

        return new PluginExtensionStatePatchResult(
            true,
            new PluginExtensionStateSnapshot(
                snapshot.Plugin,
                patch.Revision,
                new ReadOnlyDictionary<string, object?>(next)));
    }

    public static PluginExtensionStatePatchResult ApplyStateSnapshot(
        PluginExtensionStateSnapshot snapshot,
        PluginExtensionStateSnapshotEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(envelope);

        if (!string.Equals(snapshot.Plugin, envelope.Plugin, StringComparison.Ordinal) || envelope.Revision < snapshot.Revision)
            return new PluginExtensionStatePatchResult(false, snapshot);

        return new PluginExtensionStatePatchResult(
            true,
            new PluginExtensionStateSnapshot(
                snapshot.Plugin,
                envelope.Revision,
                new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>(envelope.State, StringComparer.Ordinal))));
    }

    public static PluginExtensionStatePatchResult ApplyStateEnvelope(
        PluginExtensionStateSnapshot snapshot,
        PluginExtensionProtocolParseResult parsed)
    {
        ArgumentNullException.ThrowIfNull(parsed);
        if (parsed.StatePatch is not null)
            return ApplyStatePatch(snapshot, parsed.StatePatch);
        if (parsed.StateSnapshot is not null)
            return ApplyStateSnapshot(snapshot, parsed.StateSnapshot);
        return new PluginExtensionStatePatchResult(false, snapshot);
    }

    private static void ValidateJsonObject(JsonElement element, string path, List<PluginExtensionProtocolDiagnostic> diagnostics)
    {
        foreach (var property in element.EnumerateObject())
        {
            var propertyPath = $"{path}.{property.Name}";
            if (UnsafeKeys.Contains(property.Name))
            {
                diagnostics.Add(Diagnostic("json_key_unsafe", propertyPath, "Unsafe state key is not allowed."));
                continue;
            }

            ValidateJsonValue(property.Value, propertyPath, diagnostics);
        }
    }

    private static void ValidateJsonValue(JsonElement element, string path, List<PluginExtensionProtocolDiagnostic> diagnostics)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                ValidateJsonObject(element, path, diagnostics);
                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var child in element.EnumerateArray())
                    ValidateJsonValue(child, $"{path}[{index++}]", diagnostics);
                break;
            case JsonValueKind.String:
            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
            case JsonValueKind.Null:
                break;
            default:
                diagnostics.Add(Diagnostic("json_value_invalid", path, "Value must be JSON-serializable."));
                break;
        }
    }

    private static Dictionary<string, object?> ReadObject(JsonElement element) =>
        element.EnumerateObject().ToDictionary(property => property.Name, property => ReadValue(property.Value), StringComparer.Ordinal);

    private static object? ReadValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => new ReadOnlyDictionary<string, object?>(ReadObject(element)),
        JsonValueKind.Array => element.EnumerateArray().Select(ReadValue).ToArray(),
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number when element.TryGetInt64(out var integer) => integer,
        JsonValueKind.Number when element.TryGetDouble(out var number) => number,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => null
    };

    private static string? ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static PluginExtensionProtocolDiagnostic Diagnostic(string code, string path, string message) =>
        new(code, path, message);
}
