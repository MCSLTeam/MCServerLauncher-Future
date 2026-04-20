using System.Text.Json;

namespace MCServerLauncher.Common.ProtoType.Serialization;
/// <summary>
/// Buffered JSON payload that preserves explicit-json-null vs missing-property semantics.
/// Missing property should remain C# null; explicit JSON null should become a non-null buffer with ValueKind.Null.
/// </summary>
public readonly record struct JsonPayloadBuffer(JsonElement Value)
{
    public JsonValueKind ValueKind => Value.ValueKind;

    public bool IsExplicitJsonNull => ValueKind == JsonValueKind.Null;

    public string GetRawText() => Value.GetRawText();

    /// <summary>
    /// Serializes a typed value directly to a JsonElement buffer using System.Text.Json.
    /// This is the primary method for creating wire payloads.
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
