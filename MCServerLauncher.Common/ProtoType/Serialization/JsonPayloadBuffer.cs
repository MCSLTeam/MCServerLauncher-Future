using System.Text.Json;
using NewtonsoftJson = Newtonsoft.Json;
using NewtonsoftLinq = Newtonsoft.Json.Linq;

namespace MCServerLauncher.Common.ProtoType.Serialization;

/// <summary>
/// Buffered JSON payload that preserves explicit-json-null vs missing-property semantics.
/// Missing property should remain C# null; explicit JSON null should become a non-null buffer with ValueKind.Null.
/// <para>STJ is the canonical serialization path. Newtonsoft-based APIs remain as compatibility bridges.</para>
/// </summary>
public readonly record struct JsonPayloadBuffer(JsonElement Value)
{
    public JsonValueKind ValueKind => Value.ValueKind;

    public bool IsExplicitJsonNull => ValueKind == JsonValueKind.Null;

    public string GetRawText() => Value.GetRawText();

    /// <summary>
    /// STJ-first creation path. Serializes a typed value directly to a JsonElement buffer
    /// using System.Text.Json, with no Newtonsoft dependency.
    /// This is the primary method for creating wire payloads in new code.
    /// </summary>
    public static JsonPayloadBuffer FromObject<T>(T? value, JsonSerializerOptions? options = null)
    {
        if (value is null)
        {
            using var nullDoc = JsonDocument.Parse("null");
            return new JsonPayloadBuffer(nullDoc.RootElement.Clone());
        }

        var effectiveOptions = options ?? StjResolver.CreateDefaultOptions();
        var element = System.Text.Json.JsonSerializer.SerializeToElement(value, effectiveOptions);
        return new JsonPayloadBuffer(element);
    }

    /// <summary>
    /// Compatibility bridge: creates a buffer from an untyped value via Newtonsoft serialization.
    /// Prefer the generic <see cref="FromObject{T}"/> overload for new code.
    /// </summary>
    public static JsonPayloadBuffer FromObject(object? value, NewtonsoftJson.JsonSerializerSettings? settings = null)
    {
        var serializer = settings is null
            ? NewtonsoftJson.JsonSerializer.CreateDefault()
            : NewtonsoftJson.JsonSerializer.Create(settings);

        var token = value is null ? NewtonsoftLinq.JValue.CreateNull() : NewtonsoftLinq.JToken.FromObject(value, serializer);
        using var document = JsonDocument.Parse(token.ToString(NewtonsoftJson.Formatting.None));
        return new JsonPayloadBuffer(document.RootElement.Clone());
    }

    /// <summary>
    /// Compatibility bridge: converts a Newtonsoft JToken to a buffer.
    /// </summary>
    public static implicit operator JsonPayloadBuffer(NewtonsoftLinq.JToken token)
    {
        using var document = JsonDocument.Parse(token.ToString(NewtonsoftJson.Formatting.None));
        return new JsonPayloadBuffer(document.RootElement.Clone());
    }
}

/// <summary>
/// Compatibility bridge converter for Newtonsoft serialization paths.
/// Used on EventPacket properties that still go through Newtonsoft.
/// The canonical wire-path converter is <see cref="StjJsonPayloadBufferConverter"/>.
/// </summary>
public sealed class NewtonsoftJsonPayloadBufferConverter : NewtonsoftJson.JsonConverter<JsonPayloadBuffer?>
{
    public override void WriteJson(NewtonsoftJson.JsonWriter writer, JsonPayloadBuffer? value, NewtonsoftJson.JsonSerializer serializer)
    {
        if (value is null)
        {
            writer.WriteNull();
            return;
        }

        writer.WriteRawValue(value.Value.GetRawText());
    }

    public override JsonPayloadBuffer? ReadJson(
        NewtonsoftJson.JsonReader reader,
        Type objectType,
        JsonPayloadBuffer? existingValue,
        bool hasExistingValue,
        NewtonsoftJson.JsonSerializer serializer)
    {
        if (reader.TokenType == NewtonsoftJson.JsonToken.Null)
        {
            using var nullDocument = JsonDocument.Parse("null");
            return new JsonPayloadBuffer(nullDocument.RootElement.Clone());
        }

        var token = NewtonsoftLinq.JToken.Load(reader);
        using var document = JsonDocument.Parse(token.ToString(NewtonsoftJson.Formatting.None));
        return new JsonPayloadBuffer(document.RootElement.Clone());
    }
}

/// <summary>
/// Canonical STJ wire-path converter for JsonPayloadBuffer.
/// This is the primary converter used when serializing/deserializing packets on the wire.
/// </summary>
public sealed class StjJsonPayloadBufferConverter : System.Text.Json.Serialization.JsonConverter<JsonPayloadBuffer?>
{
    public override JsonPayloadBuffer? Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return new JsonPayloadBuffer(document.RootElement.Clone());
    }

    public override void Write(Utf8JsonWriter writer, JsonPayloadBuffer? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        value.Value.Value.WriteTo(writer);
    }
}
