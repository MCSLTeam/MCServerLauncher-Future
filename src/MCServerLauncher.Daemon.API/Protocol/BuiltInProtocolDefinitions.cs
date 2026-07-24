using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization.Metadata;
using MCServerLauncher.Common.Contracts.EventRules;
using MCServerLauncher.Common.Contracts.Files;
using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Common.Contracts.Auth;
using MCServerLauncher.Common.Contracts.Operations;
using MCServerLauncher.Common.Contracts.Provisioning;
using MCServerLauncher.Common.Contracts.Serialization;
using MCServerLauncher.Common.Contracts.System;

namespace MCServerLauncher.Daemon.API.Protocol;

/// <summary>
/// The frozen built-in protocol definitions shared by daemon, daemon client, and documentation tooling.
/// </summary>
public static class BuiltInProtocolDefinitions
{
    private static readonly PermissionName Authenticated = new("*");

    private static ApplicationContractJsonContext Application => ApplicationContractJsonContext.Default;
    private static BuiltInProtocolJsonContext Protocol => BuiltInProtocolJsonContext.Default;

public static RpcDescriptor<EmptyRequest, PermissionsResult> GetAuthPermissions { get; } = Rpc("mcsl.auth.permissions.get", new("mcsl.auth.permissions.get"), Protocol.EmptyRequest, Protocol.PermissionsResult, "auth", "Get permissions", "Gets the authenticated connection permissions.");
    public static RpcDescriptor<TokenIssueRequest, TokenIssueResult> IssueToken { get; } = Rpc("mcsl.auth.token.issue", new("mcsl.auth.token.issue"), Application.TokenIssueRequest, Application.TokenIssueResult, "auth", "Issue token", "Issues an audience-bound JWT from the main token.");
    public static RpcDescriptor<EmptyRequest, PingResult> PingDaemon { get; } = Rpc("mcsl.daemon.ping", new("mcsl.daemon.ping"), Protocol.EmptyRequest, Protocol.PingResult, "daemon", "Ping daemon", "Gets the daemon time for connection-latency measurement.");
    public static RpcDescriptor<PathTransferRequest, UnitResult> CopyDirectory { get; } = Rpc("mcsl.directory.copy", new("mcsl.directory.copy"), Application.PathTransferRequest, Protocol.UnitResult, "files", "Copy directory", "Copies a contained directory.");
    public static RpcDescriptor<PathRequest, UnitResult> CreateDirectory { get; } = Rpc("mcsl.directory.create", new("mcsl.directory.create"), Application.PathRequest, Protocol.UnitResult, "files", "Create directory", "Creates a contained directory.");
    public static RpcDescriptor<DeleteDirectoryRequest, UnitResult> DeleteDirectory { get; } = Rpc("mcsl.directory.delete", new("mcsl.directory.delete"), Application.DeleteDirectoryRequest, Protocol.UnitResult, "files", "Delete directory", "Deletes a contained directory.");
    public static RpcDescriptor<PathRequest, DirectoryDetails> GetDirectoryInfo { get; } = Rpc("mcsl.directory.info.get", new("mcsl.directory.info.get"), Application.PathRequest, Application.DirectoryDetails, "files", "Get directory information", "Gets contained directory metadata and entries.");
    public static RpcDescriptor<PathTransferRequest, UnitResult> MoveDirectory { get; } = Rpc("mcsl.directory.move", new("mcsl.directory.move"), Application.PathTransferRequest, Protocol.UnitResult, "files", "Move directory", "Moves a contained directory.");
    public static RpcDescriptor<PathRenameRequest, UnitResult> RenameDirectory { get; } = Rpc("mcsl.directory.rename", new("mcsl.directory.rename"), Application.PathRenameRequest, Protocol.UnitResult, "files", "Rename directory", "Renames a contained directory.");
    public static RpcDescriptor<EventSubscriptionRequest, UnitResult> SubscribeEvent { get; } = Rpc("mcsl.event.subscribe", new("mcsl.event.subscribe"), Protocol.EventSubscriptionRequest, Protocol.UnitResult, "events", "Subscribe to event", "Subscribes the connection to a typed server event.");
    public static RpcDescriptor<EventSubscriptionRequest, UnitResult> UnsubscribeEvent { get; } = Rpc("mcsl.event.unsubscribe", new("mcsl.event.unsubscribe"), Protocol.EventSubscriptionRequest, Protocol.UnitResult, "events", "Unsubscribe from event", "Removes the connection subscription for a typed server event.");
    public static RpcDescriptor<PathTransferRequest, UnitResult> CopyFile { get; } = Rpc("mcsl.file.copy", new("mcsl.file.copy"), Application.PathTransferRequest, Protocol.UnitResult, "files", "Copy file", "Copies a contained file.");
    public static RpcDescriptor<PathRequest, UnitResult> DeleteFile { get; } = Rpc("mcsl.file.delete", new("mcsl.file.delete"), Application.PathRequest, Protocol.UnitResult, "files", "Delete file", "Deletes a contained file.");
    public static RpcDescriptor<FileSessionReference, UnitResult> CloseDownload { get; } = Rpc("mcsl.file.download.close", new("mcsl.file.download.close"), Protocol.FileSessionReference, Protocol.UnitResult, "files", "Close download", "Closes a download session.");
    public static RpcDescriptor<DownloadOpenRequest, DownloadSession> OpenDownload { get; } = Rpc("mcsl.file.download.open", new("mcsl.file.download.open"), Application.DownloadOpenRequest, Application.DownloadSession, "files", "Open download", "Opens a bounded binary download session.");
    public static RpcDescriptor<DownloadChunkRequest, DownloadReadResult> ReadDownload { get; } = Rpc("mcsl.file.download.read", new("mcsl.file.download.read"), Application.DownloadChunkRequest, Protocol.DownloadReadResult, "files", "Read download chunk", "Requests the next binary download chunk.");
    public static RpcDescriptor<PathRequest, FileDetails> GetFileInfo { get; } = Rpc("mcsl.file.info.get", new("mcsl.file.info.get"), Application.PathRequest, Application.FileDetails, "files", "Get file information", "Gets contained file metadata.");
    public static RpcDescriptor<PathTransferRequest, UnitResult> MoveFile { get; } = Rpc("mcsl.file.move", new("mcsl.file.move"), Application.PathTransferRequest, Protocol.UnitResult, "files", "Move file", "Moves a contained file.");
    public static RpcDescriptor<PathRenameRequest, UnitResult> RenameFile { get; } = Rpc("mcsl.file.rename", new("mcsl.file.rename"), Application.PathRenameRequest, Protocol.UnitResult, "files", "Rename file", "Renames a contained file.");
    public static RpcDescriptor<FileSessionReference, UnitResult> CancelUpload { get; } = Rpc("mcsl.file.upload.cancel", new("mcsl.file.upload.cancel"), Protocol.FileSessionReference, Protocol.UnitResult, "files", "Cancel upload", "Cancels an upload session.");
    public static RpcDescriptor<FileSessionReference, UnitResult> CloseUpload { get; } = Rpc("mcsl.file.upload.close", new("mcsl.file.upload.close"), Protocol.FileSessionReference, Protocol.UnitResult, "files", "Close upload", "Validates and commits an upload session.");
    public static RpcDescriptor<UploadOpenRequest, UploadSession> OpenUpload { get; } = Rpc("mcsl.file.upload.open", new("mcsl.file.upload.open"), Application.UploadOpenRequest, Application.UploadSession, "files", "Open upload", "Opens a bounded binary upload session.");
    public static RpcDescriptor<EmptyRequest, InstanceCatalogResult> GetInstanceCatalog { get; } = Rpc("mcsl.instance.catalog.get", new("mcsl.instance.catalog.get"), Protocol.EmptyRequest, Protocol.InstanceCatalogResult, "instances", "Get instance catalog", "Gets the current immutable instance catalog snapshot.");
    public static RpcDescriptor<InstanceCommandRequest, UnitResult> SendInstanceCommand { get; } = Rpc("mcsl.instance.command.send", new("mcsl.instance.command.send"), Application.InstanceCommandRequest, Protocol.UnitResult, "instances", "Send instance command", "Sends a command to a running instance.");
    public static RpcDescriptor<ConsoleOpenRequest, ConsoleSession> OpenConsole { get; } = Rpc("mcsl.instance.console.open", new("mcsl.instance.console.open"), Application.ConsoleOpenRequest, Application.ConsoleSession, "instances", "Open console", "Opens a binary console session for a running instance.");
    public static RpcDescriptor<ConsoleResizeRequest, UnitResult> ResizeConsole { get; } = Rpc("mcsl.instance.console.resize", new("mcsl.instance.console.resize"), Application.ConsoleResizeRequest, Protocol.UnitResult, "instances", "Resize console", "Resizes an open console session.");
    public static RpcDescriptor<ConsoleSessionReference, UnitResult> CloseConsole { get; } = Rpc("mcsl.instance.console.close", new("mcsl.instance.console.close"), Application.ConsoleSessionReference, Protocol.UnitResult, "instances", "Close console", "Closes an open console session.");
    public static RpcDescriptor<CreateInstanceRequest, CreateInstanceResult> CreateInstance { get; } = Rpc("mcsl.instance.create", new("mcsl.instance.create"), Application.CreateInstanceRequest, Application.CreateInstanceResult, "instances", "Create instance", "Creates and persists a new instance.");
    public static RpcDescriptor<EventRuleQuery, EventRuleSet> GetInstanceEventRules { get; } = Rpc("mcsl.instance.event-rules.get", new("mcsl.instance.event-rules.get"), Application.EventRuleQuery, Application.EventRuleSet, "events", "Get event rules", "Gets persisted event rules for an instance.");
    public static RpcDescriptor<EventRuleUpdateRequest, UnitResult> UpdateInstanceEventRules { get; } = Rpc("mcsl.instance.event-rules.update", new("mcsl.instance.event-rules.update"), Application.EventRuleUpdateRequest, Protocol.UnitResult, "events", "Update event rules", "Persists and replaces event rules for an instance.");
    public static RpcDescriptor<InstanceReference, UnitResult> HaltInstance { get; } = Rpc("mcsl.instance.halt", new("mcsl.instance.halt"), Application.InstanceReference, Protocol.UnitResult, "instances", "Halt instance", "Immediately signals an instance process to halt.");
    public static RpcDescriptor<InstanceLogQuery, InstanceLogResult> GetInstanceLog { get; } = Rpc("mcsl.instance.log.get", new("mcsl.instance.log.get"), Application.InstanceLogQuery, Application.InstanceLogResult, "instances", "Get instance log", "Gets retained log lines for an instance.");
    public static RpcDescriptor<InstanceReference, UnitResult> RemoveInstance { get; } = Rpc("mcsl.instance.remove", new("mcsl.instance.remove"), Application.InstanceReference, Protocol.UnitResult, "instances", "Remove instance", "Removes a stopped instance.");
    public static RpcDescriptor<InstanceReference, InstanceReport> GetInstanceReport { get; } = Rpc("mcsl.instance.report.get", new("mcsl.instance.report.get"), Application.InstanceReference, Application.InstanceReport, "instances", "Get instance report", "Gets the current report for an instance.");
    public static RpcDescriptor<EmptyRequest, InstanceReportList> ListInstanceReports { get; } = Rpc("mcsl.instance.report.list", new("mcsl.instance.report.list"), Protocol.EmptyRequest, Application.InstanceReportList, "instances", "List instance reports", "Gets current reports for all instances.");
    public static RpcDescriptor<InstanceReference, InstanceSettingsResult> GetInstanceSettings { get; } = Rpc("mcsl.instance.settings.get", new("mcsl.instance.settings.get"), Application.InstanceReference, Application.InstanceSettingsResult, "instances", "Get instance settings", "Gets editable settings for an instance.");
    public static RpcDescriptor<UpdateInstanceSettingsRequest, UpdateInstanceSettingsResult> UpdateInstanceSettings { get; } = Rpc("mcsl.instance.settings.update", new("mcsl.instance.settings.update"), Application.UpdateInstanceSettingsRequest, Application.UpdateInstanceSettingsResult, "instances", "Update instance settings", "Persists and applies instance settings.");
    public static RpcDescriptor<InstanceReference, UnitResult> StartInstance { get; } = Rpc("mcsl.instance.start", new("mcsl.instance.start"), Application.InstanceReference, Protocol.UnitResult, "instances", "Start instance", "Starts an instance.");
    public static RpcDescriptor<InstanceReference, UnitResult> StopInstance { get; } = Rpc("mcsl.instance.stop", new("mcsl.instance.stop"), Application.InstanceReference, Protocol.UnitResult, "instances", "Stop instance", "Requests a graceful instance stop.");
    public static RpcDescriptor<EmptyRequest, JavaRuntimeList> ListJavaRuntimes { get; } = Rpc("mcsl.java.list", new("mcsl.java.list"), Protocol.EmptyRequest, Application.JavaRuntimeList, "system", "List Java runtimes", "Lists Java runtimes available to the daemon.");
    public static RpcDescriptor<OperationListQuery, OperationListResult> ListOperations { get; } = Rpc("mcsl.operation.list", new("mcsl.operation.list"), Application.OperationListQuery, Application.OperationListResult, "operations", "List operations", "Lists retained long-running operations visible to the caller.");
    public static RpcDescriptor<OperationReference, OperationSnapshot> GetOperation { get; } = Rpc("mcsl.operation.get", new("mcsl.operation.get"), Application.OperationReference, Application.OperationSnapshot, "operations", "Get operation", "Gets an immutable snapshot of a long-running operation.");
    public static RpcDescriptor<OperationCancelRequest, OperationCancelResult> CancelOperation { get; } = Rpc("mcsl.operation.cancel", new("mcsl.operation.cancel"), Application.OperationCancelRequest, Application.OperationCancelResult, "operations", "Cancel operation", "Requests cooperative cancellation of a long-running operation.");
    public static RpcDescriptor<ProvisioningResolveRequest, ProvisioningPlanSnapshot> ResolveProvisioning { get; } = Rpc("mcsl.provisioning.resolve", new("mcsl.provisioning.resolve"), Application.ProvisioningResolveRequest, Application.ProvisioningPlanSnapshot, "provisioning", "Resolve provisioning plan", "Resolves an immutable provisioning plan for automatic providers.");
    public static RpcDescriptor<ProvisioningPlanReference, ProvisioningPlanSnapshot> GetProvisioningPlan { get; } = Rpc("mcsl.provisioning.get", new("mcsl.provisioning.get"), Application.ProvisioningPlanReference, Application.ProvisioningPlanSnapshot, "provisioning", "Get provisioning plan", "Gets an immutable provisioning plan snapshot.");
    public static RpcDescriptor<ProvisioningExecuteRequest, ProvisioningExecuteResult> ExecuteProvisioning { get; } = Rpc("mcsl.provisioning.execute", new("mcsl.provisioning.execute"), Application.ProvisioningExecuteRequest, Application.ProvisioningExecuteResult, "provisioning", "Execute provisioning plan", "Executes a ready provisioning plan as a long-running operation.");
    public static RpcDescriptor<EmptyRequest, SystemInfo> GetSystemInfo { get; } = Rpc("mcsl.system.info.get", new("mcsl.system.info.get"), Protocol.EmptyRequest, Application.SystemInfo, "system", "Get system information", "Gets daemon host system information.");
    public static RpcDescriptor<EmptyRequest, OpenRpcDocument> DiscoverRpc { get; } = Rpc("rpc.discover", new("rpc.discover"), Protocol.EmptyRequest, Protocol.OpenRpcDocument, "discovery", "Discover protocol", "Gets the frozen runtime OpenRPC protocol document.");

    public static EventDescriptor<InstanceCatalogChangedEventData, EmptyRequest> InstanceCatalogChanged { get; } = Event<InstanceCatalogChangedEventData, EmptyRequest>("mcsl.event.instance.catalog.changed", Authenticated, Protocol.InstanceCatalogChangedEventData, null, OpenRpcEventFieldPresence.Omitted, "instances", "Instance catalog changed", "Publishes an ordered instance catalog delta.");
    public static EventDescriptor<DaemonReportEventData, EmptyRequest> DaemonReport { get; } = Event<DaemonReportEventData, EmptyRequest>("mcsl.event.daemon.report", Authenticated, Protocol.DaemonReportEventData, null, OpenRpcEventFieldPresence.Omitted, "system", "Daemon report", "Publishes a periodic daemon system report.");
    public static EventDescriptor<InstanceLogEventData, InstanceLogEventMeta> InstanceLog { get; } = Event("mcsl.event.instance.log", Authenticated, Protocol.InstanceLogEventData, Protocol.InstanceLogEventMeta, OpenRpcEventFieldPresence.Required, "instances", "Instance log", "Publishes a log line emitted by an instance.");
    public static EventDescriptor<NotificationEventData, NotificationEventMeta> Notification { get; } = Event("mcsl.event.notification", Authenticated, Protocol.NotificationEventData, Protocol.NotificationEventMeta, OpenRpcEventFieldPresence.Required, "events", "Notification", "Publishes an event-rule notification.");

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

    private static ImmutableArray<RpcDescriptor> CreateRpcs() =>
        ImmutableArray.Create<RpcDescriptor>(
            GetAuthPermissions, IssueToken, PingDaemon, CopyDirectory, CreateDirectory, DeleteDirectory, GetDirectoryInfo, MoveDirectory, RenameDirectory,
            SubscribeEvent, UnsubscribeEvent, CopyFile, DeleteFile, CloseDownload, OpenDownload, ReadDownload, GetFileInfo, MoveFile, RenameFile,
            CancelUpload, CloseUpload, OpenUpload, GetInstanceCatalog, SendInstanceCommand, OpenConsole, ResizeConsole, CloseConsole, CreateInstance, GetInstanceEventRules,
            UpdateInstanceEventRules, HaltInstance, GetInstanceLog, RemoveInstance, GetInstanceReport, ListInstanceReports, GetInstanceSettings,
            UpdateInstanceSettings, StartInstance, StopInstance, ListJavaRuntimes, ListOperations, GetOperation, CancelOperation, ResolveProvisioning, GetProvisioningPlan, ExecuteProvisioning, GetSystemInfo, DiscoverRpc)
        .Sort(CompareRpcs);

    private static ImmutableArray<EventDescriptor> CreateEvents() =>
        ImmutableArray.Create<EventDescriptor>(InstanceCatalogChanged, DaemonReport, InstanceLog, Notification)
            .Sort(CompareEvents);

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
        snapshotSchema.Remove("default");

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
