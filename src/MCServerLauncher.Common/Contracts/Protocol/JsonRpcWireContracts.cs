using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using MCServerLauncher.Common.Contracts.Serialization;

namespace MCServerLauncher.Common.Contracts.Protocol;

public static class JsonRpcWireConstants
{
    public const string Version = "2.0";
    public const string UploadAcknowledgementMethod = "mcsl.file.upload.ack";
}

public enum JsonRpcRequestIdKind
{
    String,
    Integer
}

/// <summary>
/// A JSON-RPC request identifier restricted to this protocol's string-or-Int64 profile.
/// Integer identifiers retain their validated JSON token so a response can echo values such as -0 unchanged.
/// </summary>
[JsonConverter(typeof(JsonRpcRequestIdJsonConverter))]
public sealed class JsonRpcRequestId : IEquatable<JsonRpcRequestId>
{
    private const int MaximumIntegerTokenLength = 20;
    private readonly string? _stringValue;
    private readonly string? _integerToken;

    private JsonRpcRequestId(JsonRpcRequestIdKind kind, string value)
    {
        Kind = kind;
        if (kind == JsonRpcRequestIdKind.String)
        {
            _stringValue = value;
        }
        else
        {
            ValidateIntegerToken(value, out var integerValue);
            _integerToken = value;
            IntegerValue = integerValue;
        }
    }

    public JsonRpcRequestIdKind Kind { get; }

    [JsonIgnore]
    public string? StringValue => _stringValue;

    [JsonIgnore]
    public long? IntegerValue { get; }

    [JsonIgnore]
    public string? IntegerToken => _integerToken;

    public static JsonRpcRequestId FromString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new JsonRpcRequestId(JsonRpcRequestIdKind.String, value);
    }

    public static JsonRpcRequestId FromInt64(long value) =>
        new(JsonRpcRequestIdKind.Integer, value.ToString(CultureInfo.InvariantCulture));

    public bool Equals(JsonRpcRequestId? other) =>
        other is not null &&
        Kind == other.Kind &&
        StringComparer.Ordinal.Equals(_stringValue, other._stringValue) &&
        StringComparer.Ordinal.Equals(_integerToken, other._integerToken);

    public override bool Equals(object? obj) => obj is JsonRpcRequestId other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Kind, _stringValue, _integerToken);

    public override string ToString() => _stringValue ?? _integerToken!;

    internal static JsonRpcRequestId FromValidatedIntegerToken(string token) =>
        new(JsonRpcRequestIdKind.Integer, token);

    internal static void ValidateIntegerToken(string token, out long value)
    {
        ArgumentNullException.ThrowIfNull(token);
        if (token.Length is 0 or > MaximumIntegerTokenLength)
        {
            throw new JsonException("A JSON-RPC integer identifier is outside the Int64 profile.");
        }

        var digitIndex = token[0] == '-' ? 1 : 0;
        if (digitIndex == token.Length)
        {
            throw new JsonException("A JSON-RPC integer identifier requires digits.");
        }

        if (token[digitIndex] == '0' && digitIndex + 1 != token.Length)
        {
            throw new JsonException("A JSON-RPC integer identifier cannot contain leading zeroes.");
        }

        for (var index = digitIndex; index < token.Length; index++)
        {
            if (token[index] is < '0' or > '9')
            {
                throw new JsonException("A JSON-RPC identifier number must be an integer token without a fraction or exponent.");
            }
        }

        if (!long.TryParse(token, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value))
        {
            throw new JsonException("A JSON-RPC integer identifier is outside the Int64 profile.");
        }
    }
}

public sealed class JsonRpcRequestIdJsonConverter : JsonConverter<JsonRpcRequestId>
{
    public override bool HandleNull => true;

    public override JsonRpcRequestId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return JsonRpcRequestId.FromString(reader.GetString()!);
        }

        if (reader.TokenType != JsonTokenType.Number)
        {
            throw new JsonException("A JSON-RPC request identifier must be a string or signed Int64 integer.");
        }

        var byteLength = reader.HasValueSequence ? reader.ValueSequence.Length : reader.ValueSpan.Length;
        if (byteLength > 20)
        {
            throw new JsonException("A JSON-RPC integer identifier is outside the Int64 profile.");
        }

        string token;
        if (reader.HasValueSequence)
        {
            Span<byte> bytes = stackalloc byte[20];
            reader.ValueSequence.CopyTo(bytes);
            token = Encoding.UTF8.GetString(bytes[..checked((int)byteLength)]);
        }
        else
        {
            token = Encoding.UTF8.GetString(reader.ValueSpan);
        }

        return JsonRpcRequestId.FromValidatedIntegerToken(token);
    }

    public override void Write(Utf8JsonWriter writer, JsonRpcRequestId value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        switch (value.Kind)
        {
            case JsonRpcRequestIdKind.String:
                writer.WriteStringValue(value.StringValue);
                break;
            case JsonRpcRequestIdKind.Integer:
                JsonRpcRequestId.ValidateIntegerToken(value.IntegerToken!, out _);
                writer.WriteRawValue(value.IntegerToken!, skipInputValidation: false);
                break;
            default:
                throw new JsonException("A JSON-RPC request identifier has an unknown kind.");
        }
    }
}

/// <summary>
/// An immutable, validated JSON object token used only at the wire envelope boundary.
/// Catalog-owned JsonTypeInfo remains authoritative for the concrete request and result DTO.
/// </summary>
[JsonConverter(typeof(JsonRpcObjectPayloadJsonConverter))]
public sealed class JsonRpcObjectPayload
{
    private readonly byte[] _utf8Json;
    private readonly int _offset;
    private readonly int _length;

    private JsonRpcObjectPayload(byte[] utf8Json, int offset, int length)
    {
        _utf8Json = utf8Json;
        _offset = offset;
        _length = length;
    }

    public static JsonRpcObjectPayload From<T>(T value, JsonTypeInfo<T> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, typeInfo);
        ValidateSingleObject(bytes);
        return new JsonRpcObjectPayload(bytes, 0, bytes.Length);
    }

    public static JsonRpcObjectPayload From(object value, JsonTypeInfo typeInfo)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(typeInfo);
        if (value.GetType() != typeInfo.Type)
        {
            throw new ArgumentException(
                $"JSON metadata for '{typeInfo.Type}' cannot serialize a value of exact type '{value.GetType()}'.",
                nameof(typeInfo));
        }

        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, typeInfo);
        ValidateSingleObject(bytes);
        return new JsonRpcObjectPayload(bytes, 0, bytes.Length);
    }

    /// <summary>
    /// Copy a previously encoded UTF-8 JSON object into an immutable payload.
    /// Used for frozen OpenRPC <c>rpc.discover</c> responses and similar pre-encoded results.
    /// </summary>
    public static JsonRpcObjectPayload FromValidatedUtf8Object(ReadOnlyMemory<byte> utf8Json)
    {
        if (utf8Json.IsEmpty)
        {
            throw new ArgumentException("A JSON-RPC object payload cannot be empty.", nameof(utf8Json));
        }

        var copy = utf8Json.ToArray();
        ValidateSingleObject(copy);
        return new JsonRpcObjectPayload(copy, 0, copy.Length);
    }

    public object Deserialize(JsonTypeInfo typeInfo)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        ValidateMappedObjectProperties(_utf8Json.AsSpan(_offset, _length), typeInfo);
        var value = JsonSerializer.Deserialize(_utf8Json.AsSpan(_offset, _length), typeInfo);
        if (value is null || value.GetType() != typeInfo.Type)
        {
            throw new JsonException($"JSON object metadata for '{typeInfo.Type}' did not produce its exact declared type.");
        }

        return value;
    }

    private static void ValidateMappedObjectProperties(ReadOnlySpan<byte> utf8Json, JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object)
        {
            return;
        }

        var reader = new Utf8JsonReader(utf8Json);
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("A typed JSON-RPC payload must be an object.");
        }

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("A typed JSON-RPC payload must contain named properties.");
            }

            var isMapped = false;
            foreach (var property in typeInfo.Properties)
            {
                if (reader.ValueTextEquals(property.Name))
                {
                    isMapped = true;
                    break;
                }
            }

            if (!isMapped)
            {
                throw new JsonException("A typed JSON-RPC payload contains an unsupported property.");
            }

            if (!reader.Read())
            {
                throw new JsonException("A typed JSON-RPC payload ended before a property value.");
            }

            reader.Skip();
        }
    }

    internal static JsonRpcObjectPayload FromOwnedBuffer(byte[] utf8Json, int offset, int length)
        => new(utf8Json, offset, length);

    internal static JsonRpcObjectPayload FromOwnedValidatedUtf8Object(byte[] utf8Json, int offset, int length)
    {
        ArgumentNullException.ThrowIfNull(utf8Json);
        ValidateSingleObject(utf8Json.AsSpan(offset, length));
        return new JsonRpcObjectPayload(utf8Json, offset, length);
    }

    internal bool IsEmptyObject
    {
        get
        {
            var reader = new Utf8JsonReader(_utf8Json.AsSpan(_offset, _length));
            return reader.Read() &&
                   reader.TokenType == JsonTokenType.StartObject &&
                   reader.Read() &&
                   reader.TokenType == JsonTokenType.EndObject;
        }
    }

    internal void WriteTo(Utf8JsonWriter writer) =>
        writer.WriteRawValue(_utf8Json.AsSpan(_offset, _length), skipInputValidation: true);

    private static void ValidateSingleObject(ReadOnlySpan<byte> utf8Json)
    {
        var reader = new Utf8JsonReader(utf8Json);
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
        {
            throw new ArgumentException("A JSON-RPC object payload must contain one JSON object.", nameof(utf8Json));
        }

        reader.Skip();
        if (reader.Read())
        {
            throw new ArgumentException("A JSON-RPC object payload must contain exactly one JSON object.", nameof(utf8Json));
        }
    }
}

public sealed class JsonRpcObjectPayloadJsonConverter : JsonConverter<JsonRpcObjectPayload>
{
    public override JsonRpcObjectPayload Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new JsonException("Use JsonRpcWireParser for strict JSON-RPC envelope deserialization.");

    public override void Write(Utf8JsonWriter writer, JsonRpcObjectPayload value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(value);
        value.WriteTo(writer);
    }
}

[JsonConverter(typeof(JsonRpcRequestEnvelopeJsonConverter))]
public sealed class JsonRpcRequestEnvelope
{
    public JsonRpcRequestEnvelope(string method, JsonRpcRequestId? id, JsonRpcObjectPayload? @params = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        Method = method;
        Id = id;
        Params = @params;
    }

    [JsonPropertyName("jsonrpc")]
    public string JsonRpc => JsonRpcWireConstants.Version;

    public string Method { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonRpcRequestId? Id { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonRpcObjectPayload? Params { get; }

    [JsonIgnore]
    public bool IsNotification => Id is null;
}

public sealed class JsonRpcRequestEnvelopeJsonConverter : JsonConverter<JsonRpcRequestEnvelope>
{
    public override JsonRpcRequestEnvelope Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => throw new JsonException("Use JsonRpcWireParser for strict JSON-RPC envelope deserialization.");

    public override void Write(Utf8JsonWriter writer, JsonRpcRequestEnvelope value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(value);
        writer.WriteStartObject();
        writer.WriteString("jsonrpc", JsonRpcWireConstants.Version);
        writer.WriteString("method", value.Method);
        if (value.Id is not null)
        {
            writer.WritePropertyName("id");
            JsonSerializer.Serialize(
                writer,
                value.Id,
                (JsonTypeInfo<JsonRpcRequestId>)options.GetTypeInfo(typeof(JsonRpcRequestId)));
        }

        if (value.Params is not null)
        {
            writer.WritePropertyName("params");
            value.Params.WriteTo(writer);
        }

        writer.WriteEndObject();
    }
}

[JsonConverter(typeof(JsonRpcSuccessResponseEnvelopeJsonConverter))]
public sealed class JsonRpcSuccessResponseEnvelope
{
    public JsonRpcSuccessResponseEnvelope(JsonRpcRequestId id, JsonRpcObjectPayload result)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(result);
        Id = id;
        Result = result;
    }

    [JsonPropertyName("jsonrpc")]
    public string JsonRpc => JsonRpcWireConstants.Version;

    public JsonRpcRequestId Id { get; }

    public JsonRpcObjectPayload Result { get; }
}

public sealed class JsonRpcSuccessResponseEnvelopeJsonConverter : JsonConverter<JsonRpcSuccessResponseEnvelope>
{
    public override JsonRpcSuccessResponseEnvelope Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) =>
        throw new JsonException("Use JsonRpcWireParser for strict JSON-RPC envelope deserialization.");

    public override void Write(
        Utf8JsonWriter writer,
        JsonRpcSuccessResponseEnvelope value,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(value);
        writer.WriteStartObject();
        writer.WriteString("jsonrpc", JsonRpcWireConstants.Version);
        writer.WritePropertyName("id");
        JsonSerializer.Serialize(
            writer,
            value.Id,
            (JsonTypeInfo<JsonRpcRequestId>)options.GetTypeInfo(typeof(JsonRpcRequestId)));
        writer.WritePropertyName("result");
        value.Result.WriteTo(writer);
        writer.WriteEndObject();
    }
}

[JsonConverter(typeof(JsonRpcErrorResponseEnvelopeJsonConverter))]
public sealed class JsonRpcErrorResponseEnvelope
{
    public JsonRpcErrorResponseEnvelope(JsonRpcRequestId? id, JsonRpcErrorObject error)
    {
        ArgumentNullException.ThrowIfNull(error);
        Id = id;
        Error = error;
    }

    [JsonPropertyName("jsonrpc")]
    public string JsonRpc => JsonRpcWireConstants.Version;

    public JsonRpcRequestId? Id { get; }

    public JsonRpcErrorObject Error { get; }
}

public sealed class JsonRpcErrorResponseEnvelopeJsonConverter : JsonConverter<JsonRpcErrorResponseEnvelope>
{
    public override JsonRpcErrorResponseEnvelope Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) =>
        throw new JsonException("Use JsonRpcWireParser for strict JSON-RPC envelope deserialization.");

    public override void Write(
        Utf8JsonWriter writer,
        JsonRpcErrorResponseEnvelope value,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(value);
        JsonRpcErrorContractValidator.Validate(value.Error);
        writer.WriteStartObject();
        writer.WriteString("jsonrpc", JsonRpcWireConstants.Version);
        writer.WritePropertyName("id");
        if (value.Id is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            JsonSerializer.Serialize(
                writer,
                value.Id,
                (JsonTypeInfo<JsonRpcRequestId>)options.GetTypeInfo(typeof(JsonRpcRequestId)));
        }

        writer.WritePropertyName("error");
        JsonSerializer.Serialize(
            writer,
            value.Error,
            (JsonTypeInfo<JsonRpcErrorObject>)options.GetTypeInfo(typeof(JsonRpcErrorObject)));
        writer.WriteEndObject();
    }
}

public enum JsonRpcOptionalPayloadKind
{
    Missing,
    ExplicitNull,
    Value
}

/// <summary>
/// The missing/null/value state of a remote event field. Values are created through explicit JsonTypeInfo.
/// </summary>
[JsonConverter(typeof(JsonRpcOptionalPayloadJsonConverter))]
public sealed class JsonRpcOptionalPayload
{
    private readonly byte[] _utf8Json;
    private readonly int _offset;
    private readonly int _length;

    private JsonRpcOptionalPayload(JsonRpcOptionalPayloadKind kind, byte[] utf8Json, int offset, int length)
    {
        Kind = kind;
        _utf8Json = utf8Json;
        _offset = offset;
        _length = length;
    }

    public static JsonRpcOptionalPayload Missing { get; } =
        new(JsonRpcOptionalPayloadKind.Missing, [], 0, 0);

    public static JsonRpcOptionalPayload ExplicitNull { get; } =
        new(JsonRpcOptionalPayloadKind.ExplicitNull, [], 0, 0);

    public JsonRpcOptionalPayloadKind Kind { get; }

    public static JsonRpcOptionalPayload From<T>(T value, JsonTypeInfo<T> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, typeInfo);
        return bytes.AsSpan().SequenceEqual("null"u8)
            ? ExplicitNull
            : new JsonRpcOptionalPayload(JsonRpcOptionalPayloadKind.Value, bytes, 0, bytes.Length);
    }

    public T? Deserialize<T>(JsonTypeInfo<T> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        return Kind switch
        {
            JsonRpcOptionalPayloadKind.Missing =>
                throw new InvalidOperationException("A missing remote event field cannot be deserialized."),
            JsonRpcOptionalPayloadKind.ExplicitNull => default,
            JsonRpcOptionalPayloadKind.Value =>
                JsonSerializer.Deserialize<T>(_utf8Json.AsSpan(_offset, _length), typeInfo),
            _ => throw new InvalidOperationException("The remote event field has an invalid payload kind.")
        };
    }

    internal static JsonRpcOptionalPayload FromOwnedBuffer(byte[] utf8Json, int offset, int length) =>
        utf8Json.AsSpan(offset, length).SequenceEqual("null"u8)
            ? ExplicitNull
            : new JsonRpcOptionalPayload(JsonRpcOptionalPayloadKind.Value, utf8Json, offset, length);

    internal void WriteTo(Utf8JsonWriter writer) =>
        writer.WriteRawValue(_utf8Json.AsSpan(_offset, _length), skipInputValidation: false);
}

public sealed class JsonRpcOptionalPayloadJsonConverter : JsonConverter<JsonRpcOptionalPayload>
{
    public override bool HandleNull => true;

    public override JsonRpcOptionalPayload Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) =>
        throw new JsonException("Use JsonRpcWireParser for strict JSON-RPC envelope deserialization.");

    public override void Write(
        Utf8JsonWriter writer,
        JsonRpcOptionalPayload value,
        JsonSerializerOptions options)
    {
        if (value is null || value.Kind == JsonRpcOptionalPayloadKind.ExplicitNull)
        {
            writer.WriteNullValue();
            return;
        }

        if (value.Kind != JsonRpcOptionalPayloadKind.Value)
        {
            throw new JsonException("A missing remote event field has no standalone JSON representation.");
        }

        value.WriteTo(writer);
    }
}

[JsonConverter(typeof(JsonRpcRemoteEventParametersJsonConverter))]
public sealed class JsonRpcRemoteEventParameters
{
    public JsonRpcRemoteEventParameters(
        long sequence,
        long timestamp,
        JsonRpcOptionalPayload meta,
        JsonRpcOptionalPayload data)
    {
        if (sequence < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence), "A remote event sequence cannot be negative.");
        }

        if (timestamp < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timestamp), "A remote event timestamp cannot be negative.");
        }

        ArgumentNullException.ThrowIfNull(meta);
        ArgumentNullException.ThrowIfNull(data);
        Sequence = sequence;
        Timestamp = timestamp;
        Meta = meta;
        Data = data;
    }

    public long Sequence { get; }

    public long Timestamp { get; }

    public JsonRpcOptionalPayload Meta { get; }

    public JsonRpcOptionalPayload Data { get; }
}

public sealed class JsonRpcRemoteEventParametersJsonConverter : JsonConverter<JsonRpcRemoteEventParameters>
{
    public override JsonRpcRemoteEventParameters Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => throw new JsonException("Use JsonRpcWireParser for strict JSON-RPC envelope deserialization.");

    public override void Write(Utf8JsonWriter writer, JsonRpcRemoteEventParameters value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(value);
        writer.WriteStartObject();
        writer.WriteNumber("sequence", value.Sequence);
        writer.WriteNumber("timestamp", value.Timestamp);
        WriteOptional(writer, "meta", value.Meta);
        WriteOptional(writer, "data", value.Data);
        writer.WriteEndObject();
    }

    private static void WriteOptional(Utf8JsonWriter writer, string propertyName, JsonRpcOptionalPayload value)
    {
        switch (value.Kind)
        {
            case JsonRpcOptionalPayloadKind.Missing:
                return;
            case JsonRpcOptionalPayloadKind.ExplicitNull:
                writer.WriteNull(propertyName);
                return;
            case JsonRpcOptionalPayloadKind.Value:
                writer.WritePropertyName(propertyName);
                value.WriteTo(writer);
                return;
            default:
                throw new JsonException("A remote event field has an unknown presence kind.");
        }
    }
}

[JsonConverter(typeof(JsonRpcRemoteEventNotificationJsonConverter))]
public sealed class JsonRpcRemoteEventNotification
{
    public JsonRpcRemoteEventNotification(string method, JsonRpcRemoteEventParameters @params)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ArgumentNullException.ThrowIfNull(@params);
        Method = method;
        Params = @params;
    }

    [JsonPropertyName("jsonrpc")]
    public string JsonRpc => JsonRpcWireConstants.Version;

    public string Method { get; }

    public JsonRpcRemoteEventParameters Params { get; }
}

public sealed class JsonRpcRemoteEventNotificationJsonConverter : JsonConverter<JsonRpcRemoteEventNotification>
{
    public override JsonRpcRemoteEventNotification Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) =>
        throw new JsonException("Use JsonRpcWireParser for strict JSON-RPC envelope deserialization.");

    public override void Write(
        Utf8JsonWriter writer,
        JsonRpcRemoteEventNotification value,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(value);
        writer.WriteStartObject();
        writer.WriteString("jsonrpc", JsonRpcWireConstants.Version);
        writer.WriteString("method", value.Method);
        writer.WritePropertyName("params");
        JsonSerializer.Serialize(
            writer,
            value.Params,
            (JsonTypeInfo<JsonRpcRemoteEventParameters>)options.GetTypeInfo(typeof(JsonRpcRemoteEventParameters)));
        writer.WriteEndObject();
    }
}

[JsonConverter(typeof(JsonRpcUploadAcknowledgementNotificationJsonConverter))]
public sealed class JsonRpcUploadAcknowledgementNotification
{
    public JsonRpcUploadAcknowledgementNotification(UploadChunkAcknowledgement @params)
    {
        ArgumentNullException.ThrowIfNull(@params);
        Params = @params;
    }

    [JsonPropertyName("jsonrpc")]
    public string JsonRpc => JsonRpcWireConstants.Version;

    public string Method => JsonRpcWireConstants.UploadAcknowledgementMethod;

    public UploadChunkAcknowledgement Params { get; }
}

public sealed class JsonRpcUploadAcknowledgementNotificationJsonConverter
    : JsonConverter<JsonRpcUploadAcknowledgementNotification>
{
    public override JsonRpcUploadAcknowledgementNotification Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) =>
        throw new JsonException("Use JsonRpcWireParser for strict JSON-RPC envelope deserialization.");

    public override void Write(
        Utf8JsonWriter writer,
        JsonRpcUploadAcknowledgementNotification value,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(value);
        writer.WriteStartObject();
        writer.WriteString("jsonrpc", JsonRpcWireConstants.Version);
        writer.WriteString("method", JsonRpcWireConstants.UploadAcknowledgementMethod);
        writer.WritePropertyName("params");
        JsonSerializer.Serialize(
            writer,
            value.Params,
            (JsonTypeInfo<UploadChunkAcknowledgement>)options.GetTypeInfo(typeof(UploadChunkAcknowledgement)));
        writer.WriteEndObject();
    }
}

/// <summary>
/// The sole public read path for strict JSON-RPC wire envelopes. Dynamic payload tokens are copied once
/// from the caller-owned UTF-8 input into an envelope-owned buffer; concrete DTO interpretation remains
/// catalog-driven through explicit JsonTypeInfo.
/// </summary>
public enum JsonRpcRequestFailureKind
{
    ParseError,
    InvalidRequest
}

public sealed class JsonRpcRequestParseException : JsonException
{
    public JsonRpcRequestParseException(
        JsonRpcRequestFailureKind failureKind,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        if (!Enum.IsDefined(failureKind))
        {
            throw new ArgumentOutOfRangeException(nameof(failureKind));
        }

        FailureKind = failureKind;
    }

    public JsonRpcRequestFailureKind FailureKind { get; }
}

public static class JsonRpcWireParser
{
    public static JsonRpcRequestEnvelope ParseRequest(ReadOnlySpan<byte> utf8Json)
    {
        var ownedJson = utf8Json.ToArray();
        JsonTokenType rootToken;
        try
        {
            rootToken = ReadSingleRootToken(ownedJson);
        }
        catch (JsonException exception)
        {
            throw new JsonRpcRequestParseException(
                JsonRpcRequestFailureKind.ParseError,
                "The JSON-RPC request is not one complete JSON value.",
                exception);
        }

        if (rootToken != JsonTokenType.StartObject)
        {
            throw new JsonRpcRequestParseException(
                JsonRpcRequestFailureKind.InvalidRequest,
                "A JSON-RPC request must be an object.");
        }

        try
        {
            return ParseValidatedRequest(ownedJson);
        }
        catch (JsonRpcRequestParseException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new JsonRpcRequestParseException(
                JsonRpcRequestFailureKind.InvalidRequest,
                "The JSON value does not satisfy the JSON-RPC request profile.",
                exception);
        }
    }

    private static JsonRpcRequestEnvelope ParseValidatedRequest(byte[] ownedJson)
    {
        var reader = StartObject(ownedJson, "A JSON-RPC request must be an object.");
        string? version = null;
        string? method = null;
        JsonRpcRequestId? id = null;
        JsonRpcObjectPayload? parameters = null;
        var hasVersion = false;
        var hasMethod = false;
        var hasId = false;
        var hasParams = false;

        ReadObject(ref reader, (string propertyName, ref Utf8JsonReader valueReader) =>
        {
            switch (propertyName)
            {
                case "jsonrpc" when !hasVersion:
                    version = ReadString(ref valueReader, "The jsonrpc property must be a string.");
                    hasVersion = true;
                    break;
                case "method" when !hasMethod:
                    method = ReadString(ref valueReader, "The method property must be a string.");
                    hasMethod = true;
                    break;
                case "id" when !hasId:
                    id = ReadRequestId(ref valueReader, ownedJson, allowNull: false);
                    hasId = true;
                    break;
                case "params" when !hasParams:
                    parameters = ReadObjectPayload(ref valueReader, ownedJson, "The params property must be an object.");
                    hasParams = true;
                    break;
                default:
                    throw new JsonException($"Unsupported or duplicate JSON-RPC request property '{propertyName}'.");
            }
        });
        EnsureDocumentEnd(ref reader);
        RequireVersion(version, hasVersion);
        RequireMethod(method, hasMethod, eventOnly: false);
        return new JsonRpcRequestEnvelope(method!, id, parameters);
    }

    private static JsonTokenType ReadSingleRootToken(ReadOnlySpan<byte> utf8Json)
    {
        var reader = new Utf8JsonReader(utf8Json);
        if (!reader.Read())
        {
            throw new JsonException("A JSON document must contain one value.");
        }

        var rootToken = reader.TokenType;
        reader.Skip();
        if (reader.Read())
        {
            throw new JsonException("A JSON document cannot contain trailing values.");
        }

        return rootToken;
    }

    public static JsonRpcSuccessResponseEnvelope ParseSuccessResponse(ReadOnlySpan<byte> utf8Json)
    {
        var ownedJson = utf8Json.ToArray();
        var reader = StartObject(ownedJson, "A JSON-RPC success response must be an object.");
        string? version = null;
        JsonRpcRequestId? id = null;
        JsonRpcObjectPayload? result = null;
        var hasVersion = false;
        var hasId = false;
        var hasResult = false;

        ReadObject(ref reader, (string propertyName, ref Utf8JsonReader valueReader) =>
        {
            switch (propertyName)
            {
                case "jsonrpc" when !hasVersion:
                    version = ReadString(ref valueReader, "The jsonrpc property must be a string.");
                    hasVersion = true;
                    break;
                case "id" when !hasId:
                    id = ReadRequestId(ref valueReader, ownedJson, allowNull: false);
                    hasId = true;
                    break;
                case "result" when !hasResult:
                    result = ReadObjectPayload(ref valueReader, ownedJson, "A JSON-RPC result must be an object.");
                    hasResult = true;
                    break;
                default:
                    throw new JsonException($"Unsupported or duplicate JSON-RPC success property '{propertyName}'.");
            }
        });
        EnsureDocumentEnd(ref reader);
        RequireVersion(version, hasVersion);
        if (!hasId || id is null || !hasResult || result is null)
        {
            throw new JsonException("A JSON-RPC success response requires a non-null id and an object result.");
        }

        return new JsonRpcSuccessResponseEnvelope(id, result);
    }

    public static JsonRpcErrorResponseEnvelope ParseErrorResponse(ReadOnlySpan<byte> utf8Json)
    {
        var ownedJson = utf8Json.ToArray();
        var reader = StartObject(ownedJson, "A JSON-RPC error response must be an object.");
        string? version = null;
        JsonRpcRequestId? id = null;
        JsonRpcErrorObject? error = null;
        var hasVersion = false;
        var hasId = false;
        var hasError = false;

        ReadObject(ref reader, (string propertyName, ref Utf8JsonReader valueReader) =>
        {
            switch (propertyName)
            {
                case "jsonrpc" when !hasVersion:
                    version = ReadString(ref valueReader, "The jsonrpc property must be a string.");
                    hasVersion = true;
                    break;
                case "id" when !hasId:
                    id = ReadRequestId(ref valueReader, ownedJson, allowNull: true);
                    hasId = true;
                    break;
                case "error" when !hasError:
                    error = ReadErrorObject(ref valueReader, ownedJson);
                    hasError = true;
                    break;
                default:
                    throw new JsonException($"Unsupported or duplicate JSON-RPC error property '{propertyName}'.");
            }
        });
        EnsureDocumentEnd(ref reader);
        RequireVersion(version, hasVersion);
        if (!hasId || !hasError || error is null)
        {
            throw new JsonException("A JSON-RPC error response requires id and error properties.");
        }

        return new JsonRpcErrorResponseEnvelope(id, error);
    }

    public static JsonRpcRemoteEventNotification ParseRemoteEventNotification(ReadOnlySpan<byte> utf8Json)
    {
        var ownedJson = utf8Json.ToArray();
        var reader = StartObject(ownedJson, "A remote event notification must be an object.");
        string? version = null;
        string? method = null;
        JsonRpcRemoteEventParameters? parameters = null;
        var hasVersion = false;
        var hasMethod = false;
        var hasParams = false;

        ReadObject(ref reader, (string propertyName, ref Utf8JsonReader valueReader) =>
        {
            switch (propertyName)
            {
                case "jsonrpc" when !hasVersion:
                    version = ReadString(ref valueReader, "The jsonrpc property must be a string.");
                    hasVersion = true;
                    break;
                case "method" when !hasMethod:
                    method = ReadString(ref valueReader, "The method property must be a string.");
                    hasMethod = true;
                    break;
                case "params" when !hasParams:
                    parameters = ReadRemoteEventParameters(ref valueReader, ownedJson);
                    hasParams = true;
                    break;
                default:
                    throw new JsonException($"Unsupported or duplicate remote event property '{propertyName}'.");
            }
        });
        EnsureDocumentEnd(ref reader);
        RequireVersion(version, hasVersion);
        RequireMethod(method, hasMethod, eventOnly: true);
        if (!hasParams || parameters is null)
        {
            throw new JsonException("A remote event notification requires object params.");
        }

        return new JsonRpcRemoteEventNotification(method!, parameters);
    }

    public static JsonRpcUploadAcknowledgementNotification ParseUploadAcknowledgementNotification(
        ReadOnlySpan<byte> utf8Json)
    {
        var ownedJson = utf8Json.ToArray();
        var reader = StartObject(ownedJson, "An upload acknowledgement notification must be an object.");
        string? version = null;
        string? method = null;
        UploadChunkAcknowledgement? parameters = null;
        var hasVersion = false;
        var hasMethod = false;
        var hasParams = false;

        ReadObject(ref reader, (string propertyName, ref Utf8JsonReader valueReader) =>
        {
            switch (propertyName)
            {
                case "jsonrpc" when !hasVersion:
                    version = ReadString(ref valueReader, "The jsonrpc property must be a string.");
                    hasVersion = true;
                    break;
                case "method" when !hasMethod:
                    method = ReadString(ref valueReader, "The method property must be a string.");
                    hasMethod = true;
                    break;
                case "params" when !hasParams:
                    parameters = ReadUploadAcknowledgement(ref valueReader, ownedJson);
                    hasParams = true;
                    break;
                default:
                    throw new JsonException($"Unsupported or duplicate upload acknowledgement property '{propertyName}'.");
            }
        });
        EnsureDocumentEnd(ref reader);
        RequireVersion(version, hasVersion);
        if (!hasMethod || method != JsonRpcWireConstants.UploadAcknowledgementMethod)
        {
            throw new JsonException($"An upload acknowledgement method must be '{JsonRpcWireConstants.UploadAcknowledgementMethod}'.");
        }

        if (!hasParams || parameters is null)
        {
            throw new JsonException("An upload acknowledgement notification requires object params.");
        }

        return new JsonRpcUploadAcknowledgementNotification(parameters);
    }

    private delegate void PropertyReader(string propertyName, ref Utf8JsonReader reader);

    private static Utf8JsonReader StartObject(ReadOnlySpan<byte> utf8Json, string errorMessage)
    {
        var reader = new Utf8JsonReader(utf8Json);
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException(errorMessage);
        }

        return reader;
    }

    private static void ReadObject(ref Utf8JsonReader reader, PropertyReader readProperty)
    {
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("A JSON-RPC object must contain named properties.");
            }

            var propertyName = reader.GetString()!;
            if (!reader.Read())
            {
                throw new JsonException("A JSON-RPC object ended before a property value.");
            }

            readProperty(propertyName, ref reader);
        }

        throw new JsonException("A JSON-RPC object was not terminated.");
    }

    private static JsonRpcRemoteEventParameters ReadRemoteEventParameters(
        ref Utf8JsonReader reader,
        byte[] source)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Remote event params must be an object.");
        }

        long sequence = 0;
        long timestamp = 0;
        var meta = JsonRpcOptionalPayload.Missing;
        var data = JsonRpcOptionalPayload.Missing;
        var hasSequence = false;
        var hasTimestamp = false;
        var hasMeta = false;
        var hasData = false;

        ReadObject(ref reader, (string propertyName, ref Utf8JsonReader valueReader) =>
        {
            switch (propertyName)
            {
                case "sequence" when !hasSequence:
                    sequence = ReadNonNegativeInt64(ref valueReader, source, "A remote event sequence");
                    hasSequence = true;
                    break;
                case "timestamp" when !hasTimestamp:
                    timestamp = ReadNonNegativeInt64(ref valueReader, source, "A remote event timestamp");
                    hasTimestamp = true;
                    break;
                case "meta" when !hasMeta:
                    meta = ReadOptionalPayload(ref valueReader, source);
                    hasMeta = true;
                    break;
                case "data" when !hasData:
                    data = ReadOptionalPayload(ref valueReader, source);
                    hasData = true;
                    break;
                default:
                    throw new JsonException($"Unsupported or duplicate remote event param '{propertyName}'.");
            }
        });

        if (!hasSequence || !hasTimestamp)
        {
            throw new JsonException("Remote event params require sequence and timestamp.");
        }

        return new JsonRpcRemoteEventParameters(sequence, timestamp, meta, data);
    }

    private static UploadChunkAcknowledgement ReadUploadAcknowledgement(
        ref Utf8JsonReader reader,
        byte[] source)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Upload acknowledgement params must be an object.");
        }

        Guid sessionId = default;
        long offset = 0;
        int length = 0;
        UploadChunkAcknowledgementStatus status = default;
        JsonRpcErrorObject? error = null;
        var hasSessionId = false;
        var hasOffset = false;
        var hasLength = false;
        var hasStatus = false;
        var hasError = false;

        ReadObject(ref reader, (string propertyName, ref Utf8JsonReader valueReader) =>
        {
            switch (propertyName)
            {
                case "session_id" when !hasSessionId:
                    if (valueReader.TokenType != JsonTokenType.String ||
                        !Guid.TryParse(valueReader.GetString(), out sessionId) ||
                        sessionId == Guid.Empty)
                    {
                        throw new JsonException("An upload acknowledgement requires a non-empty GUID session_id.");
                    }

                    hasSessionId = true;
                    break;
                case "offset" when !hasOffset:
                    offset = ReadNonNegativeInt64(ref valueReader, source, "An upload acknowledgement offset");
                    hasOffset = true;
                    break;
                case "length" when !hasLength:
                    var parsedLength = ReadNonNegativeInt64(ref valueReader, source, "An upload acknowledgement length");
                    if (parsedLength > int.MaxValue)
                    {
                        throw new JsonException("An upload acknowledgement length exceeds Int32.");
                    }

                    length = checked((int)parsedLength);
                    hasLength = true;
                    break;
                case "status" when !hasStatus:
                    status = ReadUploadStatus(ref valueReader);
                    hasStatus = true;
                    break;
                case "error" when !hasError:
                    error = ReadErrorObject(ref valueReader, source);
                    hasError = true;
                    break;
                default:
                    throw new JsonException($"Unsupported or duplicate upload acknowledgement param '{propertyName}'.");
            }
        });

        if (!hasSessionId || !hasOffset || !hasLength || !hasStatus)
        {
            throw new JsonException("Upload acknowledgement params require session_id, offset, length, and status.");
        }

        if ((status == UploadChunkAcknowledgementStatus.Accepted && hasError) ||
            (status == UploadChunkAcknowledgementStatus.Rejected && !hasError))
        {
            throw new JsonException("Upload acknowledgement error presence must match its status.");
        }

        return new UploadChunkAcknowledgement(sessionId, offset, length, status, error);
    }

    private static JsonRpcErrorObject ReadErrorObject(ref Utf8JsonReader reader, byte[] source)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("A JSON-RPC error must be an object.");
        }

        int code = 0;
        string? message = null;
        JsonRpcErrorData? data = null;
        var hasCode = false;
        var hasMessage = false;
        var hasData = false;

        ReadObject(ref reader, (string propertyName, ref Utf8JsonReader valueReader) =>
        {
            switch (propertyName)
            {
                case "code" when !hasCode:
                    var parsedCode = ReadStrictInt64(ref valueReader, source, "A JSON-RPC error code");
                    if (parsedCode is < int.MinValue or > int.MaxValue)
                    {
                        throw new JsonException("A JSON-RPC error code must fit Int32.");
                    }

                    code = checked((int)parsedCode);
                    hasCode = true;
                    break;
                case "message" when !hasMessage:
                    message = ReadString(ref valueReader, "A JSON-RPC error message must be a string.");
                    if (string.IsNullOrWhiteSpace(message))
                    {
                        throw new JsonException("A JSON-RPC error message cannot be empty.");
                    }

                    hasMessage = true;
                    break;
                case "data" when !hasData:
                    data = ReadErrorData(ref valueReader, source);
                    hasData = true;
                    break;
                default:
                    throw new JsonException($"Unsupported or duplicate JSON-RPC error property '{propertyName}'.");
            }
        });

        if (!hasCode || !hasMessage || !hasData || data is null)
        {
            throw new JsonException("A JSON-RPC error requires code, message, and data.");
        }

        return new JsonRpcErrorObject(code, message!, data);
    }

    private static JsonRpcErrorData ReadErrorData(ref Utf8JsonReader reader, byte[] source)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("JSON-RPC error data must be an object.");
        }

        string? daemonErrorCode = null;
        DaemonErrorWireKind daemonErrorKind = default;
        string? correlationId = null;
        JsonElement? details = null;
        ProtocolOwnerIdentity? originPlugin = null;
        ProtocolOwnerIdentity? executionOwner = null;
        var hasDaemonErrorCode = false;
        var hasDaemonErrorKind = false;
        var hasCorrelationId = false;
        var hasDetails = false;
        var hasOriginPlugin = false;
        var hasExecutionOwner = false;

        ReadObject(ref reader, (string propertyName, ref Utf8JsonReader valueReader) =>
        {
            switch (propertyName)
            {
                case "daemon_error_code" when !hasDaemonErrorCode:
                    daemonErrorCode = ReadString(ref valueReader, "daemon_error_code must be a string.");
                    if (string.IsNullOrWhiteSpace(daemonErrorCode))
                    {
                        throw new JsonException("daemon_error_code cannot be empty.");
                    }

                    hasDaemonErrorCode = true;
                    break;
                case "daemon_error_kind" when !hasDaemonErrorKind:
                    daemonErrorKind = valueReader.TokenType == JsonTokenType.String
                        ? valueReader.GetString() switch
                        {
                            "validation" => DaemonErrorWireKind.Validation,
                            "not_found" => DaemonErrorWireKind.NotFound,
                            "conflict" => DaemonErrorWireKind.Conflict,
                            "permission" => DaemonErrorWireKind.Permission,
                            "storage" => DaemonErrorWireKind.Storage,
                            "transport" => DaemonErrorWireKind.Transport,
                            "internal" => DaemonErrorWireKind.Internal,
                            _ => throw new JsonException("daemon_error_kind is not a supported value.")
                        }
                        : throw new JsonException("daemon_error_kind must be a string.");
                    hasDaemonErrorKind = true;
                    break;
                case "correlation_id" when !hasCorrelationId:
                    correlationId = ReadString(ref valueReader, "correlation_id must be a string.");
                    if (string.IsNullOrWhiteSpace(correlationId))
                    {
                        throw new JsonException("correlation_id cannot be empty.");
                    }

                    hasCorrelationId = true;
                    break;
                case "details" when !hasDetails:
                    details = ReadDetails(ref valueReader, source);
                    hasDetails = true;
                    break;
                case "origin_plugin" when !hasOriginPlugin:
                    originPlugin = ReadOwnerIdentity(ref valueReader);
                    hasOriginPlugin = true;
                    break;
                case "execution_owner" when !hasExecutionOwner:
                    executionOwner = ReadOwnerIdentity(ref valueReader);
                    hasExecutionOwner = true;
                    break;
                default:
                    throw new JsonException($"Unsupported or duplicate JSON-RPC error data property '{propertyName}'.");
            }
        });

        if (!hasCorrelationId || !hasDaemonErrorKind)
        {
            throw new JsonException("JSON-RPC error data requires daemon_error_kind and correlation_id.");
        }

        return new JsonRpcErrorData(
            daemonErrorCode,
            daemonErrorKind,
            correlationId!,
            details,
            originPlugin,
            executionOwner);
    }

    private static ProtocolOwnerIdentity ReadOwnerIdentity(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("A protocol owner identity must be an object.");
        }

        string? id = null;
        string? version = null;
        var hasId = false;
        var hasVersion = false;

        ReadObject(ref reader, (string propertyName, ref Utf8JsonReader valueReader) =>
        {
            switch (propertyName)
            {
                case "id" when !hasId:
                    id = ReadString(ref valueReader, "A protocol owner id must be a string.");
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        throw new JsonException("A protocol owner id cannot be empty.");
                    }

                    hasId = true;
                    break;
                case "version" when !hasVersion:
                    version = ReadString(ref valueReader, "A protocol owner version must be a string.");
                    if (string.IsNullOrWhiteSpace(version))
                    {
                        throw new JsonException("A protocol owner version cannot be empty.");
                    }

                    hasVersion = true;
                    break;
                default:
                    throw new JsonException($"Unsupported or duplicate protocol owner property '{propertyName}'.");
            }
        });

        if (!hasId || !hasVersion)
        {
            throw new JsonException("A protocol owner identity requires id and version.");
        }

        return new ProtocolOwnerIdentity(id!, version!);
    }

    private static JsonElement ReadDetails(ref Utf8JsonReader reader, byte[] source)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            throw new JsonException("JSON-RPC error details must be omitted instead of null.");
        }

        var start = checked((int)reader.TokenStartIndex);
        ValidateDynamicValue(ref reader);
        var end = checked((int)reader.BytesConsumed);
        return JsonSerializer.Deserialize(
            source.AsSpan(start, end - start),
            BuiltInProtocolJsonContext.Default.JsonElement);
    }

    private static void ValidateDynamicValue(ref Utf8JsonReader reader)
    {
        if (reader.TokenType == JsonTokenType.StartObject)
        {
            var propertyNames = new HashSet<string>(StringComparer.Ordinal);
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    return;
                }

                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    throw new JsonException("A details object must contain named properties.");
                }

                var propertyName = reader.GetString()!;
                if (!propertyNames.Add(propertyName))
                {
                    throw new JsonException($"Duplicate details property '{propertyName}'.");
                }

                if (!reader.Read())
                {
                    throw new JsonException("A details object ended before a property value.");
                }

                ValidateDynamicValue(ref reader);
            }

            throw new JsonException("A details object was not terminated.");
        }

        if (reader.TokenType == JsonTokenType.StartArray)
        {
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray)
                {
                    return;
                }

                ValidateDynamicValue(ref reader);
            }

            throw new JsonException("A details array was not terminated.");
        }

        if (reader.TokenType is JsonTokenType.PropertyName or JsonTokenType.EndObject or JsonTokenType.EndArray)
        {
            throw new JsonException("A details value is invalid.");
        }
    }

    private static JsonRpcObjectPayload ReadObjectPayload(
        ref Utf8JsonReader reader,
        byte[] source,
        string errorMessage)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException(errorMessage);
        }

        var token = ConsumeCurrentValue(ref reader, source);
        return JsonRpcObjectPayload.FromOwnedBuffer(source, token.Offset, token.Length);
    }

    private static JsonRpcOptionalPayload ReadOptionalPayload(
        ref Utf8JsonReader reader,
        byte[] source)
    {
        var token = ConsumeCurrentValue(ref reader, source);
        return JsonRpcOptionalPayload.FromOwnedBuffer(source, token.Offset, token.Length);
    }

    private static JsonRpcRequestId? ReadRequestId(
        ref Utf8JsonReader reader,
        byte[] source,
        bool allowNull)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            if (allowNull)
            {
                return null;
            }

            throw new JsonException("An explicit null request id is not allowed by this JSON-RPC profile.");
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            return JsonRpcRequestId.FromString(reader.GetString()!);
        }

        if (reader.TokenType != JsonTokenType.Number)
        {
            throw new JsonException("A JSON-RPC request id must be a string or signed Int64 integer.");
        }

        var token = ConsumeCurrentValue(ref reader, source);
        if (token.Length > 20)
        {
            throw new JsonException("A JSON-RPC integer identifier is outside the Int64 profile.");
        }

        return JsonRpcRequestId.FromValidatedIntegerToken(
            Encoding.UTF8.GetString(source.AsSpan(token.Offset, token.Length)));
    }

    private static long ReadNonNegativeInt64(
        ref Utf8JsonReader reader,
        byte[] source,
        string description)
    {
        if (reader.TokenType != JsonTokenType.Number)
        {
            throw new JsonException($"{description} must be a non-negative Int64 integer.");
        }

        var token = ConsumeCurrentValue(ref reader, source);
        if (token.Length is 0 or > 19)
        {
            throw new JsonException($"{description} must be a non-negative Int64 integer.");
        }

        long value = 0;
        foreach (var character in source.AsSpan(token.Offset, token.Length))
        {
            if (character is < (byte)'0' or > (byte)'9')
            {
                throw new JsonException($"{description} must be a non-negative Int64 integer.");
            }

            try
            {
                value = checked((value * 10) + character - (byte)'0');
            }
            catch (OverflowException exception)
            {
                throw new JsonException($"{description} exceeds Int64.", exception);
            }
        }

        return value;
    }

    private static long ReadStrictInt64(
        ref Utf8JsonReader reader,
        byte[] source,
        string description)
    {
        if (reader.TokenType != JsonTokenType.Number)
        {
            throw new JsonException($"{description} must be an Int64 integer.");
        }

        var token = ConsumeCurrentValue(ref reader, source);
        if (token.Length > 20)
        {
            throw new JsonException($"{description} exceeds Int64.");
        }

        var raw = Encoding.UTF8.GetString(source.AsSpan(token.Offset, token.Length));
        try
        {
            JsonRpcRequestId.ValidateIntegerToken(raw, out var value);
            return value;
        }
        catch (JsonException exception)
        {
            throw new JsonException($"{description} must be an Int64 integer.", exception);
        }
    }

    private static UploadChunkAcknowledgementStatus ReadUploadStatus(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("An upload acknowledgement status must be a string.");
        }

        return reader.GetString() switch
        {
            "accepted" => UploadChunkAcknowledgementStatus.Accepted,
            "rejected" => UploadChunkAcknowledgementStatus.Rejected,
            _ => throw new JsonException("An upload acknowledgement status must be accepted or rejected.")
        };
    }

    private static string ReadString(ref Utf8JsonReader reader, string errorMessage) =>
        reader.TokenType == JsonTokenType.String
            ? reader.GetString()!
            : throw new JsonException(errorMessage);

    private static JsonTokenSlice ConsumeCurrentValue(
        ref Utf8JsonReader reader,
        byte[] source)
    {
        var start = checked((int)reader.TokenStartIndex);
        if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
        {
            reader.Skip();
        }

        var end = checked((int)reader.BytesConsumed);
        if (start < 0 || end < start || end > source.Length)
        {
            throw new JsonException("A JSON-RPC token exceeded its input boundary.");
        }

        return new JsonTokenSlice(start, end - start);
    }

    private readonly record struct JsonTokenSlice(int Offset, int Length);

    private static void RequireVersion(string? version, bool hasVersion)
    {
        if (!hasVersion || version != JsonRpcWireConstants.Version)
        {
            throw new JsonException("The jsonrpc property must be exactly '2.0'.");
        }
    }

    private static void RequireMethod(string? method, bool hasMethod, bool eventOnly)
    {
        if (!hasMethod || !IsDottedIdentifier(method!) ||
            (eventOnly && !IsEventMethod(method!)))
        {
            throw new JsonException(eventOnly
                ? "A remote event method must be a valid mcsl.event.* identifier."
                : "A JSON-RPC method must be a valid dotted identifier.");
        }
    }

    private static bool IsEventMethod(string value)
    {
        if (value.StartsWith("mcsl.event.", StringComparison.Ordinal))
        {
            return value.Length > "mcsl.event.".Length;
        }

        if (!value.StartsWith("plugin.", StringComparison.Ordinal))
        {
            return false;
        }

        var separatorIndex = value.IndexOf(".event.", "plugin.".Length, StringComparison.Ordinal);
        while (separatorIndex >= 0)
        {
            var pluginId = value.AsSpan("plugin.".Length, separatorIndex - "plugin.".Length);
            var eventName = value.AsSpan(separatorIndex + ".event.".Length);
            if (IsPluginId(pluginId) && eventName.Length > 0 && IsDottedIdentifier(eventName))
            {
                return true;
            }

            separatorIndex = value.IndexOf(".event.", separatorIndex + 1, StringComparison.Ordinal);
        }

        return false;
    }

    private static bool IsPluginId(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty)
        {
            return false;
        }

        var segmentStart = true;
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            var isLetter = character is >= 'a' and <= 'z';
            var isDigit = character is >= '0' and <= '9';
            if (character == '.')
            {
                if (segmentStart || index == value.Length - 1 || value[index - 1] == '-')
                {
                    return false;
                }

                segmentStart = true;
                continue;
            }

            if ((segmentStart && !isLetter && !isDigit) ||
                (!isLetter && !isDigit && character != '-') ||
                (index == value.Length - 1 && character == '-'))
            {
                return false;
            }

            segmentStart = false;
        }

        return true;
    }

    private static bool IsDottedIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var segmentStart = true;
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            var isAsciiLetter = character is >= 'a' and <= 'z';
            var isDigit = character is >= '0' and <= '9';
            if (character == '.')
            {
                if (segmentStart || index == value.Length - 1 || value[index - 1] is '-' or '_')
                {
                    return false;
                }

                segmentStart = true;
                continue;
            }

            if ((segmentStart && !isAsciiLetter && !isDigit) ||
                (!isAsciiLetter && !isDigit && character is not '-' and not '_') ||
                (index == value.Length - 1 && character is '-' or '_'))
            {
                return false;
            }

            segmentStart = false;
        }

        return true;
    }

    private static bool IsDottedIdentifier(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty)
        {
            return false;
        }

        var segmentStart = true;
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            var isAsciiLetter = character is >= 'a' and <= 'z';
            var isDigit = character is >= '0' and <= '9';
            if (character == '.')
            {
                if (segmentStart || index == value.Length - 1 || value[index - 1] is '-' or '_')
                {
                    return false;
                }

                segmentStart = true;
                continue;
            }

            if ((segmentStart && !isAsciiLetter && !isDigit) ||
                (!isAsciiLetter && !isDigit && character is not '-' and not '_') ||
                (index == value.Length - 1 && character is '-' or '_'))
            {
                return false;
            }

            segmentStart = false;
        }

        return true;
    }

    private static void EnsureDocumentEnd(ref Utf8JsonReader reader)
    {
        if (reader.Read())
        {
            throw new JsonException("A JSON-RPC envelope must contain exactly one JSON object.");
        }
    }
}
