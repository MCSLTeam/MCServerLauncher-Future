using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization.Metadata;
using MCServerLauncher.Common.Contracts.EventRules;
using MCServerLauncher.Common.Contracts.Files;
using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Common.Contracts.Serialization;
using MCServerLauncher.Common.Contracts.System;

namespace MCServerLauncher.Daemon.API.Protocol;

/// <summary>
/// The frozen built-in protocol definitions shared by daemon, daemon client, and documentation tooling.
/// </summary>
public static class BuiltInProtocolDefinitions
{
    private static readonly PermissionName Authenticated = new("*");

    public static ImmutableArray<RpcDescriptor> Rpcs { get; } = CreateRpcs();

    public static ImmutableArray<EventDescriptor> Events { get; } = CreateEvents();

    public static OpenRpcDocument CreateDocument(string daemonVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(daemonVersion);

        return ProtocolDocumentBuilder.Create(
            new OpenRpcInfo("MCServerLauncher daemon", daemonVersion),
            Rpcs,
            Events);
    }

    private static ImmutableArray<RpcDescriptor> CreateRpcs()
    {
        var application = ApplicationContractJsonContext.Default;
        var protocol = BuiltInProtocolJsonContext.Default;

        return ImmutableArray.Create<RpcDescriptor>(
            Rpc("mcsl.auth.permissions.get", Authenticated, protocol.EmptyRequest, protocol.PermissionsResult, "auth", "Get permissions", "Gets the authenticated connection permissions."),
            Rpc("mcsl.daemon.ping", Authenticated, protocol.EmptyRequest, protocol.PingResult, "daemon", "Ping daemon", "Gets the daemon time for connection-latency measurement."),
            Rpc("mcsl.directory.create", new PermissionName("mcsl.daemon.file.create.directory"), application.PathRequest, protocol.UnitResult, "files", "Create directory", "Creates a contained directory."),
            Rpc("mcsl.directory.delete", new PermissionName("mcsl.daemon.file.delete.directory"), application.DeleteDirectoryRequest, protocol.UnitResult, "files", "Delete directory", "Deletes a contained directory."),
            Rpc("mcsl.directory.info.get", new PermissionName("mcsl.daemon.file.info.directory"), application.PathRequest, application.DirectoryDetails, "files", "Get directory information", "Gets contained directory metadata and entries."),
            Rpc("mcsl.directory.move", new PermissionName("mcsl.daemon.file.move.directory"), application.PathTransferRequest, protocol.UnitResult, "files", "Move directory", "Moves a contained directory."),
            Rpc("mcsl.directory.rename", new PermissionName("mcsl.daemon.file.rename.directory"), application.PathRenameRequest, protocol.UnitResult, "files", "Rename directory", "Renames a contained directory."),
            Rpc("mcsl.event.subscribe", Authenticated, protocol.EventSubscriptionRequest, protocol.UnitResult, "events", "Subscribe to event", "Subscribes the connection to a typed server event."),
            Rpc("mcsl.event.unsubscribe", Authenticated, protocol.EventSubscriptionRequest, protocol.UnitResult, "events", "Unsubscribe from event", "Removes the connection subscription for a typed server event."),
            Rpc("mcsl.directory.copy", new PermissionName("mcsl.daemon.file.copy.directory"), application.PathTransferRequest, protocol.UnitResult, "files", "Copy directory", "Copies a contained directory."),
            Rpc("mcsl.file.copy", new PermissionName("mcsl.daemon.file.copy.file"), application.PathTransferRequest, protocol.UnitResult, "files", "Copy file", "Copies a contained file."),
            Rpc("mcsl.file.delete", new PermissionName("mcsl.daemon.file.delete.file"), application.PathRequest, protocol.UnitResult, "files", "Delete file", "Deletes a contained file."),
            Rpc("mcsl.file.download.close", new PermissionName("mcsl.daemon.file.download"), protocol.FileSessionReference, protocol.UnitResult, "files", "Close download", "Closes a download session."),
            Rpc("mcsl.file.download.open", new PermissionName("mcsl.daemon.file.download"), application.DownloadOpenRequest, application.DownloadSession, "files", "Open download", "Opens a bounded binary download session."),
            Rpc("mcsl.file.download.read", new PermissionName("mcsl.daemon.file.download"), application.DownloadChunkRequest, protocol.DownloadReadResult, "files", "Read download chunk", "Requests the next binary download chunk."),
            Rpc("mcsl.file.info.get", new PermissionName("mcsl.daemon.file.info.file"), application.PathRequest, application.FileDetails, "files", "Get file information", "Gets contained file metadata."),
            Rpc("mcsl.file.move", new PermissionName("mcsl.daemon.file.move.file"), application.PathTransferRequest, protocol.UnitResult, "files", "Move file", "Moves a contained file."),
            Rpc("mcsl.file.rename", new PermissionName("mcsl.daemon.file.rename.file"), application.PathRenameRequest, protocol.UnitResult, "files", "Rename file", "Renames a contained file."),
            Rpc("mcsl.file.upload.cancel", new PermissionName("mcsl.daemon.file.upload"), protocol.FileSessionReference, protocol.UnitResult, "files", "Cancel upload", "Cancels an upload session."),
            Rpc("mcsl.file.upload.close", new PermissionName("mcsl.daemon.file.upload"), protocol.FileSessionReference, protocol.UnitResult, "files", "Close upload", "Validates and commits an upload session."),
            Rpc("mcsl.file.upload.open", new PermissionName("mcsl.daemon.file.upload"), application.UploadOpenRequest, application.UploadSession, "files", "Open upload", "Opens a bounded binary upload session."),
            Rpc("mcsl.instance.catalog.get", Authenticated, protocol.EmptyRequest, protocol.InstanceCatalogResult, "instances", "Get instance catalog", "Gets the current immutable instance catalog snapshot."),
            Rpc("mcsl.instance.command.send", Authenticated, application.InstanceCommandRequest, protocol.UnitResult, "instances", "Send instance command", "Sends a command to a running instance."),
            Rpc("mcsl.instance.create", Authenticated, application.CreateInstanceRequest, application.CreateInstanceResult, "instances", "Create instance", "Creates and persists a new instance."),
            Rpc("mcsl.instance.event-rules.get", Authenticated, application.EventRuleQuery, application.EventRuleSet, "events", "Get event rules", "Gets persisted event rules for an instance."),
            Rpc("mcsl.instance.event-rules.update", Authenticated, application.EventRuleUpdateRequest, protocol.UnitResult, "events", "Update event rules", "Persists and replaces event rules for an instance."),
            Rpc("mcsl.instance.halt", Authenticated, application.InstanceReference, protocol.UnitResult, "instances", "Halt instance", "Immediately signals an instance process to halt."),
            Rpc("mcsl.instance.log.get", Authenticated, application.InstanceLogQuery, application.InstanceLogResult, "instances", "Get instance log", "Gets retained log lines for an instance."),
            Rpc("mcsl.instance.remove", Authenticated, application.InstanceReference, protocol.UnitResult, "instances", "Remove instance", "Removes a stopped instance."),
            Rpc("mcsl.instance.report.get", Authenticated, application.InstanceReference, application.InstanceReport, "instances", "Get instance report", "Gets the current report for an instance."),
            Rpc("mcsl.instance.report.list", Authenticated, protocol.EmptyRequest, application.InstanceReportList, "instances", "List instance reports", "Gets current reports for all instances."),
            Rpc("mcsl.instance.settings.get", Authenticated, application.InstanceReference, application.InstanceSettingsResult, "instances", "Get instance settings", "Gets editable settings for an instance."),
            Rpc("mcsl.instance.settings.update", Authenticated, application.UpdateInstanceSettingsRequest, application.UpdateInstanceSettingsResult, "instances", "Update instance settings", "Persists and applies instance settings."),
            Rpc("mcsl.instance.start", Authenticated, application.InstanceReference, protocol.UnitResult, "instances", "Start instance", "Starts an instance."),
            Rpc("mcsl.instance.stop", Authenticated, application.InstanceReference, protocol.UnitResult, "instances", "Stop instance", "Requests a graceful instance stop."),
            Rpc("mcsl.java.list", new PermissionName("mcsl.daemon.java_list"), protocol.EmptyRequest, application.JavaRuntimeList, "system", "List Java runtimes", "Lists Java runtimes available to the daemon."),
            Rpc("mcsl.system.info.get", Authenticated, protocol.EmptyRequest, application.SystemInfo, "system", "Get system information", "Gets daemon host system information."),
            Rpc("rpc.discover", Authenticated, protocol.EmptyRequest, protocol.OpenRpcDocument, "discovery", "Discover protocol", "Gets the frozen runtime OpenRPC protocol document."))
        .Sort(CompareRpcs);
    }

    private static ImmutableArray<EventDescriptor> CreateEvents()
    {
        var protocol = BuiltInProtocolJsonContext.Default;

        return ImmutableArray.Create<EventDescriptor>(
            Event<InstanceCatalogChangedEventData, EmptyRequest>(
                "mcsl.event.instance.catalog.changed",
                Authenticated,
                protocol.InstanceCatalogChangedEventData,
                null,
                OpenRpcEventFieldPresence.Omitted,
                "instances",
                "Instance catalog changed",
                "Publishes an ordered instance catalog delta."),
            Event<DaemonReportEventData, EmptyRequest>(
                "mcsl.event.daemon.report",
                Authenticated,
                protocol.DaemonReportEventData,
                null,
                OpenRpcEventFieldPresence.Omitted,
                "system",
                "Daemon report",
                "Publishes a periodic daemon system report."),
            Event<InstanceLogEventData, InstanceLogEventMeta>(
                "mcsl.event.instance.log",
                Authenticated,
                protocol.InstanceLogEventData,
                protocol.InstanceLogEventMeta,
                OpenRpcEventFieldPresence.Required,
                "instances",
                "Instance log",
                "Publishes a log line emitted by an instance."),
            Event<NotificationEventData, NotificationEventMeta>(
                "mcsl.event.notification",
                Authenticated,
                protocol.NotificationEventData,
                protocol.NotificationEventMeta,
                OpenRpcEventFieldPresence.Required,
                "events",
                "Notification",
                "Publishes an event-rule notification."))
        .Sort(CompareEvents);
    }

    private static RpcDescriptor<TRequest, TResult> Rpc<TRequest, TResult>(
        string method,
        PermissionName permission,
        JsonTypeInfo<TRequest> requestTypeInfo,
        JsonTypeInfo<TResult> resultTypeInfo,
        string category,
        string summary,
        string description) =>
        new(
            new RpcMethod(method),
            permission,
            requestTypeInfo,
            resultTypeInfo,
            false,
            new RpcDocumentation(
                category,
                summary,
                description,
                $"mcsl.schema.{method}.request",
                $"mcsl.schema.{method}.result"));

    private static EventDescriptor<TData, TMeta> Event<TData, TMeta>(
        string name,
        PermissionName permission,
        JsonTypeInfo<TData> dataTypeInfo,
        JsonTypeInfo<TMeta>? metaTypeInfo,
        OpenRpcEventFieldPresence metaPresence,
        string category,
        string summary,
        string description) =>
        new(
            new EventName(name),
            permission,
            dataTypeInfo,
            metaTypeInfo,
            OpenRpcEventFieldPresence.Required,
            metaPresence,
            new EventDocumentation(
                category,
                summary,
                description,
                $"mcsl.schema.{name}.data",
                metaPresence == OpenRpcEventFieldPresence.Omitted ? null : $"mcsl.schema.{name}.meta"));

    private static int CompareRpcs(RpcDescriptor? left, RpcDescriptor? right) =>
        StringComparer.Ordinal.Compare(left?.Method.Value, right?.Method.Value);

    private static int CompareEvents(EventDescriptor? left, EventDescriptor? right) =>
        StringComparer.Ordinal.Compare(left?.Name.Value, right?.Name.Value);
}

/// <summary>
/// Builds an inline-schema OpenRPC document directly from registered descriptor metadata.
/// </summary>
public static class ProtocolDocumentBuilder
{
    internal const string JsonRpcErrorDataSchemaId = "mcsl.schema.json-rpc.error.data";

    public static OpenRpcDocument Create(
        OpenRpcInfo info,
        ImmutableArray<RpcDescriptor> rpcs,
        ImmutableArray<EventDescriptor> events)
    {
        ArgumentNullException.ThrowIfNull(info);
        if (rpcs.IsDefault)
        {
            throw new ArgumentException("An RPC descriptor list cannot be default.", nameof(rpcs));
        }

        if (events.IsDefault)
        {
            throw new ArgumentException("An event descriptor list cannot be default.", nameof(events));
        }

        var methods = ImmutableArray.CreateBuilder<OpenRpcMethod>(rpcs.Length);
        foreach (var descriptor in rpcs.Sort(CompareRpcs))
        {
            methods.Add(CreateMethod(descriptor));
        }

        var documentEvents = ImmutableArray.CreateBuilder<OpenRpcEvent>(events.Length);
        foreach (var descriptor in events.Sort(CompareEvents))
        {
            documentEvents.Add(CreateEvent(descriptor));
        }

        var errorDataSchema = ExportSchema(
            BuiltInProtocolJsonContext.Default.JsonRpcErrorData,
            JsonRpcErrorDataSchemaId);
        using var componentsDocument = JsonDocument.Parse(new JsonObject
        {
            ["schemas"] = new JsonObject
            {
                [JsonRpcErrorDataSchemaId] = JsonNode.Parse(errorDataSchema.GetRawText())
            }
        }.ToJsonString());
        return new OpenRpcDocument("1.3.2", info, methods.MoveToImmutable(), documentEvents.MoveToImmutable())
        {
            JsonComponents = componentsDocument.RootElement
        };
    }

    private static OpenRpcMethod CreateMethod(RpcDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        var documentation = descriptor.Documentation ??
                            throw new ArgumentException("An RPC descriptor requires documentation metadata.", nameof(descriptor));

        var parameters = CreateParameters(ExportSchema(descriptor.RequestTypeInfo, documentation.RequestSchemaId));
        var result = new OpenRpcContentDescriptor(
            "result",
            ExportSchema(descriptor.ResultTypeInfo, documentation.ResultSchemaId),
            true,
            documentation.Summary,
            documentation.Description);

        return new OpenRpcMethod(
            descriptor.Method.Value,
            parameters,
            result,
            descriptor.Permission.Value,
            descriptor.AllowNotification,
            documentation.Summary,
            documentation.Description);
    }

    private static OpenRpcEvent CreateEvent(EventDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        var documentation = descriptor.Documentation ??
                            throw new ArgumentException("An event descriptor requires documentation metadata.", nameof(descriptor));

        var data = CreateEventField(
            descriptor.DataPresence,
            descriptor.DataTypeInfo,
            documentation.DataSchemaId);
        var meta = CreateEventField(
            descriptor.MetaPresence,
            descriptor.MetaTypeInfo,
            documentation.MetaSchemaId);

        return new OpenRpcEvent(
            descriptor.Name.Value,
            descriptor.Permission.Value,
            data,
            meta,
            documentation.Summary,
            documentation.Description);
    }

    private static OpenRpcEventField CreateEventField(
        OpenRpcEventFieldPresence presence,
        JsonTypeInfo? typeInfo,
        string? schemaId)
    {
        if (presence == OpenRpcEventFieldPresence.Omitted)
        {
            if (typeInfo is not null || schemaId is not null)
            {
                throw new ArgumentException("An omitted event field cannot define schema metadata.");
            }

            return new OpenRpcEventField(presence, null);
        }

        ArgumentNullException.ThrowIfNull(typeInfo);
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaId);
        return new OpenRpcEventField(presence, ExportSchema(typeInfo, schemaId));
    }

    private static ImmutableArray<OpenRpcContentDescriptor> CreateParameters(JsonElement requestSchema)
    {
        if (!requestSchema.TryGetProperty("properties", out var properties))
        {
            throw new InvalidOperationException("An RPC request schema must be an object schema with named properties.");
        }

        var required = new HashSet<string>(StringComparer.Ordinal);
        if (requestSchema.TryGetProperty("required", out var requiredNames))
        {
            foreach (var requiredName in requiredNames.EnumerateArray())
            {
                required.Add(requiredName.GetString() ?? throw new InvalidOperationException("A required RPC parameter name cannot be null."));
            }
        }

        var parameterProperties = new List<JsonProperty>();
        foreach (var property in properties.EnumerateObject())
        {
            parameterProperties.Add(property);
        }

        parameterProperties.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Name, right.Name));
        var parameters = ImmutableArray.CreateBuilder<OpenRpcContentDescriptor>(parameterProperties.Count);
        foreach (var property in parameterProperties)
        {
            parameters.Add(new OpenRpcContentDescriptor(
                property.Name,
                property.Value,
                required.Contains(property.Name),
                GetOptionalString(property.Value, "title"),
                GetOptionalString(property.Value, "description")));
        }

        return parameters.MoveToImmutable();
    }

    private static string? GetOptionalString(JsonElement value, string propertyName) =>
        value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static JsonElement ExportSchema(JsonTypeInfo typeInfo, string schemaId)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaId);

        var objectSchema = CreateRootSchema(typeInfo);
        objectSchema["$id"] = schemaId;

        using var document = JsonDocument.Parse(objectSchema.ToJsonString());
        return document.RootElement.Clone();
    }

    private static JsonObject CreateRootSchema(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Type == typeof(EventSubscriptionRequest))
        {
            return new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["event"] = new JsonObject { ["type"] = "string" },
                    ["meta"] = new JsonObject
                    {
                        ["anyOf"] = new JsonArray
                        {
                            new JsonObject { ["type"] = "null" },
                            new JsonObject { ["type"] = "object" }
                        }
                    }
                },
                ["required"] = new JsonArray("event"),
                ["additionalProperties"] = false
            };
        }

        if (typeInfo.Type == typeof(EmptyRequest) || typeInfo.Type == typeof(UnitResult))
        {
            return new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject(),
                ["additionalProperties"] = false
            };
        }

        JsonNode schema = typeInfo.GetJsonSchemaAsNode(new JsonSchemaExporterOptions
        {
            TreatNullObliviousAsNonNullable = true,
            TransformSchemaNode = static (context, node) =>
                context.PropertyInfo is { IsSetNullable: false }
                    ? ExcludeExplicitNull(node)
                    : node
        });
        if (schema is JsonValue booleanSchema && booleanSchema.TryGetValue<bool>(out _))
        {
            throw new InvalidOperationException($"The schema for '{typeInfo.Type}' must be explicitly modeled.");
        }

        var objectSchema = schema as JsonObject ??
                           throw new InvalidOperationException($"The schema for '{typeInfo.Type}' must be an object schema.");

        if (typeInfo.Type == typeof(InstanceCatalogChangedEventData))
        {
            ApplyInstanceCatalogChangeWireSemantics(objectSchema);
        }

        return objectSchema;
    }

    private static JsonNode ExcludeExplicitNull(JsonNode schema)
    {
        if (schema is JsonValue value && value.TryGetValue<bool>(out var allowsAnyValue) && allowsAnyValue)
        {
            return new JsonObject
            {
                ["not"] = new JsonObject { ["type"] = "null" }
            };
        }

        if (schema is not JsonObject schemaObject || schemaObject["type"] is not JsonArray types)
        {
            return schema;
        }

        for (var index = types.Count - 1; index >= 0; index--)
        {
            if (types[index]?.GetValue<string>() == "null")
            {
                types.RemoveAt(index);
            }
        }

        if (types.Count == 1)
        {
            schemaObject["type"] = types[0]!.DeepClone();
        }

        return schemaObject;
    }

    private static void ApplyInstanceCatalogChangeWireSemantics(JsonObject schema)
    {
        const string operationPropertyName = "operation";
        const string snapshotPropertyName = "snapshot";

        var properties = schema["properties"] as JsonObject ??
                         throw new InvalidOperationException("The instance catalog change schema must define properties.");
        var snapshotSchema = properties[snapshotPropertyName] as JsonObject ??
                             throw new InvalidOperationException("The instance catalog change schema must define snapshot.");
        RemoveNullType(snapshotSchema, snapshotPropertyName);

        var required = schema["required"] as JsonArray ??
                       throw new InvalidOperationException("The instance catalog change schema must define required properties.");
        for (var index = required.Count - 1; index >= 0; index--)
        {
            if (required[index]?.GetValue<string>() == snapshotPropertyName)
            {
                required.RemoveAt(index);
            }
        }

        schema["allOf"] = new JsonArray(
            CreateOperationCondition(
                operationPropertyName,
                GetOperationWireValue(InstanceCatalogChangeOperation.Upsert),
                new JsonObject { ["required"] = new JsonArray(snapshotPropertyName) }),
            CreateOperationCondition(
                operationPropertyName,
                GetOperationWireValue(InstanceCatalogChangeOperation.Remove),
                new JsonObject
                {
                    ["not"] = new JsonObject { ["required"] = new JsonArray(snapshotPropertyName) }
                }));
    }

    private static JsonObject CreateOperationCondition(
        string operationPropertyName,
        JsonNode operationWireValue,
        JsonObject thenSchema) =>
        new()
        {
            ["if"] = new JsonObject
            {
                ["properties"] = new JsonObject
                {
                    [operationPropertyName] = new JsonObject { ["const"] = operationWireValue }
                },
                ["required"] = new JsonArray(operationPropertyName)
            },
            ["then"] = thenSchema
        };

    private static JsonNode GetOperationWireValue(InstanceCatalogChangeOperation operation) =>
        JsonSerializer.SerializeToNode(operation, BuiltInProtocolJsonContext.Default.InstanceCatalogChangeOperation) ??
        throw new InvalidOperationException($"The wire value for '{operation}' cannot be null.");

    private static void RemoveNullType(JsonObject propertySchema, string propertyName)
    {
        if (propertySchema["type"] is not JsonArray types)
        {
            throw new InvalidOperationException($"The schema for '{propertyName}' must expose nullable type alternatives.");
        }

        for (var index = types.Count - 1; index >= 0; index--)
        {
            if (types[index]?.GetValue<string>() == "null")
            {
                types.RemoveAt(index);
            }
        }

        if (types.Count != 1)
        {
            throw new InvalidOperationException($"The schema for '{propertyName}' must have one non-null wire type.");
        }

        propertySchema["type"] = types[0]!.DeepClone();
    }

    private static int CompareRpcs(RpcDescriptor? left, RpcDescriptor? right) =>
        StringComparer.Ordinal.Compare(left?.Method.Value, right?.Method.Value);

    private static int CompareEvents(EventDescriptor? left, EventDescriptor? right) =>
        StringComparer.Ordinal.Compare(left?.Name.Value, right?.Name.Value);
}
