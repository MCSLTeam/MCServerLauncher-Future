using System.Text.Json;
using NewtonsoftJson = Newtonsoft.Json;
using NewtonsoftLinq = Newtonsoft.Json.Linq;

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

    public static JsonPayloadBuffer FromObject(object? value, NewtonsoftJson.JsonSerializerSettings? settings = null)
    {
        var serializer = settings is null
            ? NewtonsoftJson.JsonSerializer.CreateDefault()
            : NewtonsoftJson.JsonSerializer.Create(settings);

        var token = value is null ? NewtonsoftLinq.JValue.CreateNull() : NewtonsoftLinq.JToken.FromObject(value, serializer);
        using var document = JsonDocument.Parse(token.ToString(NewtonsoftJson.Formatting.None));
        return new JsonPayloadBuffer(document.RootElement.Clone());
    }

    public static implicit operator JsonPayloadBuffer(NewtonsoftLinq.JToken token)
    {
        using var document = JsonDocument.Parse(token.ToString(NewtonsoftJson.Formatting.None));
        return new JsonPayloadBuffer(document.RootElement.Clone());
    }
}

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
