using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCServerLauncher.Common.Contracts.System;
using MCServerLauncher.Common.ProtoType.Instance;

namespace MCServerLauncher.Common.Contracts.Protocol;

/// <summary>
/// The stable request shape for built-in RPC methods that do not take parameters.
/// </summary>
public sealed record EmptyRequest;

/// <summary>
/// The stable success result for built-in RPC methods that do not return data.
/// </summary>
public sealed record UnitResult;

public sealed record PingResult(long Time);

public sealed record PermissionsResult(ImmutableArray<string> Permissions);

[JsonConverter(typeof(EventSubscriptionRequestJsonConverter))]
public sealed class EventSubscriptionRequest
{
    public EventSubscriptionRequest(string @event)
        : this(@event, EventMetaFilter.Missing)
    {
    }

    public EventSubscriptionRequest(string @event, EventMetaFilter meta)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(@event);
        ArgumentNullException.ThrowIfNull(meta);

        Event = @event;
        Meta = meta;
    }

    public string Event { get; }

    public EventMetaFilter Meta { get; }
}

public enum EventMetaFilterKind
{
    Missing,
    ExplicitNull,
    Object
}

/// <summary>
/// The wire-level state of an event-subscription metadata filter. Object bytes are a transient input only;
/// the daemon catalog later converts them into concrete, canonical typed-ledger bytes.
/// </summary>
public sealed class EventMetaFilter : IEquatable<EventMetaFilter>
{
    private EventMetaFilter(EventMetaFilterKind kind, ImmutableArray<byte> objectUtf8Json)
    {
        Kind = kind;
        ObjectUtf8Json = objectUtf8Json;
    }

    public static EventMetaFilter Missing { get; } = new(EventMetaFilterKind.Missing, ImmutableArray<byte>.Empty);

    public static EventMetaFilter ExplicitNull { get; } = new(EventMetaFilterKind.ExplicitNull, ImmutableArray<byte>.Empty);

    public EventMetaFilterKind Kind { get; }

    public ImmutableArray<byte> ObjectUtf8Json { get; }

    public static EventMetaFilter FromObject(ReadOnlySpan<byte> utf8Json)
    {
        using var document = JsonDocument.Parse(
            utf8Json.ToArray(),
            new JsonDocumentOptions { AllowDuplicateProperties = false });
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("An event subscription metadata filter must be an object.", nameof(utf8Json));
        }

        return new EventMetaFilter(
            EventMetaFilterKind.Object,
            ImmutableArray.CreateRange(utf8Json.ToArray()));
    }

    public bool Equals(EventMetaFilter? other) =>
        other is not null &&
        Kind == other.Kind &&
        ObjectUtf8Json.AsSpan().SequenceEqual(other.ObjectUtf8Json.AsSpan());

    public override bool Equals(object? obj) => obj is EventMetaFilter other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);
        foreach (var value in ObjectUtf8Json)
        {
            hash.Add(value);
        }

        return hash.ToHashCode();
    }
}

public sealed class JsonRpcErrorObject
{
    public JsonRpcErrorObject(int code, string message, JsonRpcErrorData data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentNullException.ThrowIfNull(data);

        Code = code;
        Message = message;
        Data = data;
        JsonRpcErrorContractValidator.Validate(this);
    }

    public int Code { get; }

    public string Message { get; }

    public JsonRpcErrorData Data { get; }
}

public enum DaemonErrorWireKind
{
    Validation,
    NotFound,
    Conflict,
    Permission,
    Storage,
    Transport,
    Internal
}

public sealed class JsonRpcErrorData
{
    private string? _daemonErrorCode;
    private DaemonErrorWireKind _daemonErrorKind;
    private string _correlationId = null!;
    private JsonElement? _details;
    private ProtocolOwnerIdentity? _originPlugin;
    private ProtocolOwnerIdentity? _executionOwner;

    public JsonRpcErrorData(
        string? daemonErrorCode,
        DaemonErrorWireKind daemonErrorKind,
        string correlationId,
        JsonElement? details,
        ProtocolOwnerIdentity? originPlugin,
        ProtocolOwnerIdentity? executionOwner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        if (daemonErrorCode is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(daemonErrorCode);
        }

        if (!Enum.IsDefined(daemonErrorKind))
        {
            throw new ArgumentOutOfRangeException(nameof(daemonErrorKind));
        }

        if (details is { } detailsValue)
        {
            JsonRpcErrorContractValidator.ValidateDetails(detailsValue);
        }

        _daemonErrorCode = daemonErrorCode;
        _daemonErrorKind = daemonErrorKind;
        _correlationId = correlationId;
        _details = details?.Clone();
        _originPlugin = originPlugin;
        _executionOwner = executionOwner;
    }

    [JsonConstructor]
    internal JsonRpcErrorData()
    {
    }

    [JsonInclude]
    [JsonPropertyName("daemon_error_code")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [DisallowNull]
    public string? DaemonErrorCode
    {
        get => _daemonErrorCode;
        internal set
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            _daemonErrorCode = value;
        }
    }

    [JsonInclude]
    [JsonRequired]
    [JsonPropertyName("daemon_error_kind")]
    public DaemonErrorWireKind DaemonErrorKind
    {
        get => _daemonErrorKind;
        internal set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            _daemonErrorKind = value;
        }
    }

    [JsonInclude]
    [JsonRequired]
    [JsonPropertyName("correlation_id")]
    public string CorrelationId
    {
        get => _correlationId;
        internal set
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            _correlationId = value;
        }
    }

    [JsonInclude]
    [JsonPropertyName("details")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [DisallowNull]
    public JsonElement? Details
    {
        get => _details?.Clone();
        internal set
        {
            ArgumentNullException.ThrowIfNull(value);
            JsonRpcErrorContractValidator.ValidateDetails(value.Value);
            _details = value.Value.Clone();
        }
    }

    [JsonInclude]
    [JsonPropertyName("origin_plugin")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [DisallowNull]
    public ProtocolOwnerIdentity? OriginPlugin
    {
        get => _originPlugin;
        internal set => _originPlugin = value ?? throw new ArgumentNullException(nameof(value));
    }

    [JsonInclude]
    [JsonPropertyName("execution_owner")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [DisallowNull]
    public ProtocolOwnerIdentity? ExecutionOwner
    {
        get => _executionOwner;
        internal set => _executionOwner = value ?? throw new ArgumentNullException(nameof(value));
    }
}

public sealed class ProtocolOwnerIdentity
{
    public ProtocolOwnerIdentity(string id, string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        Id = id;
        Version = version;
    }

    public string Id { get; }

    public string Version { get; }
}

internal static class JsonRpcErrorContractValidator
{
    public static void Validate(JsonRpcErrorObject error)
    {
        ArgumentNullException.ThrowIfNull(error);
        ArgumentException.ThrowIfNullOrWhiteSpace(error.Message);
        ArgumentNullException.ThrowIfNull(error.Data);
        ArgumentException.ThrowIfNullOrWhiteSpace(error.Data.CorrelationId);
        if (!Enum.IsDefined(error.Data.DaemonErrorKind))
        {
            throw new ArgumentOutOfRangeException(nameof(error));
        }

        if (error.Data.DaemonErrorCode is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(error.Data.DaemonErrorCode);
        }

        if (error.Data.Details is { } details)
        {
            ValidateDetails(details);
        }

        ValidateOwner(error.Data.OriginPlugin);
        ValidateOwner(error.Data.ExecutionOwner);
    }

    public static void ValidateDetails(JsonElement details)
    {
        if (details.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            throw new ArgumentException("JSON-RPC error details must be a non-null JSON value.", nameof(details));
        }

        ValidateValue(details);
    }

    private static void ValidateOwner(ProtocolOwnerIdentity? owner)
    {
        if (owner is null)
        {
            return;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(owner.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner.Version);
    }

    private static void ValidateValue(JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                var propertyNames = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in value.EnumerateObject())
                {
                    if (!propertyNames.Add(property.Name))
                    {
                        throw new ArgumentException(
                            $"JSON-RPC error details contain duplicate property '{property.Name}'.",
                            nameof(value));
                    }

                    ValidateValue(property.Value);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in value.EnumerateArray())
                {
                    ValidateValue(item);
                }

                break;
            case JsonValueKind.Undefined:
                throw new ArgumentException("JSON-RPC error details contain an undefined JSON value.", nameof(value));
            case JsonValueKind.String:
            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
            case JsonValueKind.Null:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(value), value.ValueKind, "Unknown JSON value kind.");
        }
    }
}

public enum UploadChunkAcknowledgementStatus
{
    Accepted,
    Rejected
}

/// <summary>
/// Connection-owned control acknowledgement for a received upload binary frame. It never enters public event routing.
/// </summary>
public sealed class UploadChunkAcknowledgement
{
    public UploadChunkAcknowledgement(
        Guid sessionId,
        long offset,
        int length,
        UploadChunkAcknowledgementStatus status,
        JsonRpcErrorObject? error)
    {
        if (sessionId == Guid.Empty)
        {
            throw new ArgumentException("An upload acknowledgement session identifier cannot be empty.", nameof(sessionId));
        }

        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "An upload acknowledgement offset cannot be negative.");
        }

        if (length < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "An upload acknowledgement length cannot be negative.");
        }

        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), "An upload acknowledgement status must be defined.");
        }

        if (status == UploadChunkAcknowledgementStatus.Accepted && error is not null)
        {
            throw new ArgumentException("An accepted upload acknowledgement cannot contain an error.", nameof(error));
        }

        if (status == UploadChunkAcknowledgementStatus.Rejected && error is null)
        {
            throw new ArgumentNullException(nameof(error), "A rejected upload acknowledgement requires a terminal error.");
        }

        SessionId = sessionId;
        Offset = offset;
        Length = length;
        Status = status;
        Error = error;
    }

    public Guid SessionId { get; }

    public long Offset { get; }

    public int Length { get; }

    public UploadChunkAcknowledgementStatus Status { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonRpcErrorObject? Error { get; }
}

public sealed class FileSessionReference
{
    public FileSessionReference(Guid sessionId)
    {
        if (sessionId == Guid.Empty)
        {
            throw new ArgumentException("A file session identifier cannot be empty.", nameof(sessionId));
        }

        SessionId = sessionId;
    }

    public Guid SessionId { get; }
}

/// <summary>
/// JSON-RPC metadata emitted before the corresponding download binary frame. It does not carry bytes.
/// </summary>
public sealed class DownloadReadResult
{
    public DownloadReadResult(Guid sessionId, long offset, int length, bool isFinal)
    {
        if (sessionId == Guid.Empty)
        {
            throw new ArgumentException("A download read session identifier cannot be empty.", nameof(sessionId));
        }

        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "A download read offset cannot be negative.");
        }

        if (length < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "A download read length cannot be negative.");
        }

        SessionId = sessionId;
        Offset = offset;
        Length = length;
        IsFinal = isFinal;
    }

    public Guid SessionId { get; }

    public long Offset { get; }

    public int Length { get; }

    public bool IsFinal { get; }
}

public sealed record InstanceCatalogItem
{
    public InstanceCatalogItem(
        Guid instanceId,
        string name,
        InstanceType instanceType,
        string version,
        InstanceStatus status,
        bool readyTimedOut)
    {
        if (!Enum.IsDefined(instanceType))
        {
            throw new ArgumentOutOfRangeException(nameof(instanceType), "An instance catalog item type must be defined.");
        }

        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), "An instance catalog item status must be defined.");
        }

        InstanceId = instanceId;
        Name = name;
        InstanceType = instanceType;
        Version = version;
        Status = status;
        ReadyTimedOut = readyTimedOut;
    }

    public Guid InstanceId { get; init; }

    public string Name { get; init; }

    public InstanceType InstanceType { get; init; }

    public string Version { get; init; }

    public InstanceStatus Status { get; init; }

    public bool ReadyTimedOut { get; init; }
}

public sealed class InstanceCatalogResult
{
    public InstanceCatalogResult(long version, ImmutableArray<InstanceCatalogItem> items)
    {
        if (version < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(version), "An instance catalog version cannot be negative.");
        }

        if (items.IsDefault)
        {
            throw new ArgumentException("An instance catalog item list cannot be default.", nameof(items));
        }

        var identifiers = new HashSet<Guid>();
        foreach (var item in items)
        {
            if (item is null || item.InstanceId == Guid.Empty)
            {
                throw new ArgumentException("An instance catalog cannot contain null or empty-identifier items.", nameof(items));
            }

            if (!identifiers.Add(item.InstanceId))
            {
                throw new ArgumentException("An instance catalog cannot contain duplicate identifiers.", nameof(items));
            }
        }

        Version = version;
        Items = items.OrderBy(item => item.InstanceId).ToImmutableArray();
    }

    public long Version { get; }

    public ImmutableArray<InstanceCatalogItem> Items { get; }
}

public enum InstanceCatalogChangeOperation
{
    Upsert,
    Remove
}

public sealed class InstanceCatalogChangedEventData
{
    public InstanceCatalogChangedEventData(
        long version,
        InstanceCatalogChangeOperation operation,
        Guid instanceId,
        InstanceCatalogItem? snapshot = null)
    {
        if (version < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(version), "An instance catalog version cannot be negative.");
        }

        if (instanceId == Guid.Empty)
        {
            throw new ArgumentException("An instance catalog change identifier cannot be empty.", nameof(instanceId));
        }

        if (!Enum.IsDefined(operation))
        {
            throw new ArgumentOutOfRangeException(nameof(operation), "An instance catalog change operation must be defined.");
        }

        if (operation == InstanceCatalogChangeOperation.Upsert &&
            (snapshot is null || snapshot.InstanceId != instanceId))
        {
            throw new ArgumentException("An upsert catalog change requires a snapshot for the same instance identifier.", nameof(snapshot));
        }

        if (operation == InstanceCatalogChangeOperation.Remove && snapshot is not null)
        {
            throw new ArgumentException("A remove catalog change cannot contain a snapshot.", nameof(snapshot));
        }

        Version = version;
        Operation = operation;
        InstanceId = instanceId;
        Snapshot = snapshot;
    }

    public long Version { get; }

    public InstanceCatalogChangeOperation Operation { get; }

    public Guid InstanceId { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public InstanceCatalogItem? Snapshot { get; }
}

public sealed record InstanceLogEventMeta(Guid InstanceId);

public sealed record InstanceLogEventData(string Log);

public sealed record DaemonReportEventData(SystemInfo SystemInfo, long StartTimestamp);

public sealed class NotificationEventMeta
{
    public NotificationEventMeta(Guid sourceInstanceId, Guid ruleId)
    {
        if (sourceInstanceId == Guid.Empty)
        {
            throw new ArgumentException("A notification source instance identifier cannot be empty.", nameof(sourceInstanceId));
        }

        if (ruleId == Guid.Empty)
        {
            throw new ArgumentException("A notification rule identifier cannot be empty.", nameof(ruleId));
        }

        SourceInstanceId = sourceInstanceId;
        RuleId = ruleId;
    }

    public Guid SourceInstanceId { get; }

    public Guid RuleId { get; }
}

public sealed record NotificationEventData(string Title, string Message, string Severity);

public sealed record OpenRpcInfo(string Title, string Version);

public sealed class OpenRpcContentDescriptor
{
    public OpenRpcContentDescriptor(
        string name,
        JsonElement schema,
        bool required,
        string? summary = null,
        string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name;
        Schema = schema.Clone();
        Required = required;
        Summary = summary;
        Description = description;
    }

    public string Name { get; }

    public JsonElement Schema { get; }

    public bool Required { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Summary { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; }
}

public sealed class OpenRpcMethod
{
    public OpenRpcMethod(
        string name,
        ImmutableArray<OpenRpcContentDescriptor> @params,
        OpenRpcContentDescriptor result,
        string permission,
        bool allowNotification,
        string? summary = null,
        string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);
        ArgumentNullException.ThrowIfNull(result);
        if (@params.IsDefault)
        {
            throw new ArgumentException("An OpenRPC method parameter list cannot be default.", nameof(@params));
        }

        Name = name;
        Params = @params;
        Result = result;
        Permission = permission;
        AllowNotification = allowNotification;
        Summary = summary;
        Description = description;
    }

    public string Name { get; }

    public ImmutableArray<OpenRpcContentDescriptor> Params { get; }

    public OpenRpcContentDescriptor Result { get; }

    [JsonPropertyName("paramStructure")]
    public string ParamStructure => "by-name";

    [JsonPropertyName("x-mcsl-permission")]
    public string Permission { get; }

    [JsonPropertyName("x-mcsl-allow-notification")]
    public bool AllowNotification { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Summary { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; }
}

public enum OpenRpcEventFieldPresence
{
    Omitted,
    Required,
    Optional
}

public sealed class OpenRpcEventField
{
    public OpenRpcEventField(OpenRpcEventFieldPresence presence, JsonElement? schema = null)
    {
        if (!Enum.IsDefined(presence))
        {
            throw new ArgumentOutOfRangeException(nameof(presence), "An OpenRPC event field presence must be defined.");
        }

        if (presence == OpenRpcEventFieldPresence.Omitted && schema is not null)
        {
            throw new ArgumentException("An omitted event field cannot contain a schema.", nameof(schema));
        }

        if (presence != OpenRpcEventFieldPresence.Omitted && schema is null)
        {
            throw new ArgumentNullException(nameof(schema), "A present event field requires a schema.");
        }

        Presence = presence;
        Schema = schema?.Clone();
    }

    public OpenRpcEventFieldPresence Presence { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Schema { get; }
}

public sealed class OpenRpcEvent
{
    public OpenRpcEvent(
        string name,
        string permission,
        OpenRpcEventField data,
        OpenRpcEventField meta,
        string? summary = null,
        string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(meta);

        Name = name;
        Permission = permission;
        Data = data;
        Meta = meta;
        Summary = summary;
        Description = description;
    }

    public string Name { get; }

    public string Permission { get; }

    public OpenRpcEventField Data { get; }

    public OpenRpcEventField Meta { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Summary { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; }
}

public sealed class OpenRpcDocument
{
    private JsonElement _components;

    public OpenRpcDocument(
        string openRpc,
        OpenRpcInfo info,
        ImmutableArray<OpenRpcMethod> methods,
        ImmutableArray<OpenRpcEvent> events)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(openRpc);
        ArgumentNullException.ThrowIfNull(info);
        if (methods.IsDefault)
        {
            throw new ArgumentException("An OpenRPC method list cannot be default.", nameof(methods));
        }

        if (events.IsDefault)
        {
            throw new ArgumentException("An OpenRPC event list cannot be default.", nameof(events));
        }

        OpenRpc = openRpc;
        Info = info;
        Methods = methods;
        Events = events;
        using var emptyComponents = JsonDocument.Parse("{\"schemas\":{}}");
        _components = emptyComponents.RootElement.Clone();
    }

    [JsonPropertyName("openrpc")]
    public string OpenRpc { get; }

    public OpenRpcInfo Info { get; }

    public ImmutableArray<OpenRpcMethod> Methods { get; }

    [JsonIgnore]
    public JsonElement Components => _components.Clone();

    [JsonInclude]
    [JsonRequired]
    [JsonPropertyName("components")]
    internal JsonElement JsonComponents
    {
        get => _components.Clone();
        init
        {
            if (value.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException("OpenRPC components must be an object.", nameof(value));
            }

            _components = value.Clone();
        }
    }

    [JsonPropertyName("x-mcsl-events")]
    public ImmutableArray<OpenRpcEvent> Events { get; }
}

public sealed class EventSubscriptionRequestJsonConverter : JsonConverter<EventSubscriptionRequest>
{
    public override EventSubscriptionRequest Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("An event subscription request must be a JSON object.");
        }

        string? eventName = null;
        var meta = EventMetaFilter.Missing;
        var hasEvent = false;
        var hasMeta = false;

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("An event subscription request must contain named properties.");
            }

            var propertyName = reader.GetString();
            if (!reader.Read())
            {
                throw new JsonException("An event subscription request ended before a property value.");
            }

            switch (propertyName)
            {
                case "event" when !hasEvent:
                    eventName = reader.TokenType == JsonTokenType.String
                        ? reader.GetString()
                        : throw new JsonException("An event subscription request event must be a string.");
                    hasEvent = true;
                    break;
                case "meta" when !hasMeta:
                    meta = ReadMeta(ref reader);
                    hasMeta = true;
                    break;
                default:
                    throw new JsonException($"Unsupported event subscription request property '{propertyName}'.");
            }
        }

        if (!hasEvent || string.IsNullOrWhiteSpace(eventName))
        {
            throw new JsonException("An event subscription request requires a non-empty event property.");
        }

        return new EventSubscriptionRequest(eventName, meta);
    }

    public override void Write(Utf8JsonWriter writer, EventSubscriptionRequest value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(value);

        writer.WriteStartObject();
        writer.WriteString("event", value.Event);
        switch (value.Meta.Kind)
        {
            case EventMetaFilterKind.Missing:
                break;
            case EventMetaFilterKind.ExplicitNull:
                writer.WriteNull("meta");
                break;
            case EventMetaFilterKind.Object:
                writer.WritePropertyName("meta");
                writer.WriteRawValue(value.Meta.ObjectUtf8Json.AsSpan(), skipInputValidation: false);
                break;
            default:
                throw new JsonException("An event subscription request has an unknown metadata filter kind.");
        }

        writer.WriteEndObject();
    }

    private static EventMetaFilter ReadMeta(ref Utf8JsonReader reader)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return EventMetaFilter.ExplicitNull;
        }

        using var document = JsonDocument.ParseValue(ref reader);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("An event subscription metadata filter must be null or an object.");
        }

        return EventMetaFilter.FromObject(Encoding.UTF8.GetBytes(document.RootElement.GetRawText()));
    }
}
