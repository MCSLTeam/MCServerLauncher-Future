using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MCServerLauncher.Common.ProtoType.Serialization;

/// <summary>
/// STJ converter for Guid that falls back to Guid.Empty on invalid strings (parity with Newtonsoft GuidJsonConverter)
/// </summary>
public sealed class GuidStjConverter : JsonConverter<Guid>
{
    public override Guid Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var str = reader.GetString();
            return Guid.TryParse(str, out var result) ? result : Guid.Empty;
        }
        throw new JsonException($"Cannot convert {reader.TokenType} to Guid");
    }

    public override void Write(Utf8JsonWriter writer, Guid value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }

    public override Guid ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return Guid.TryParse(reader.GetString(), out var result) ? result : Guid.Empty;
    }

    public override void WriteAsPropertyName(Utf8JsonWriter writer, Guid value, JsonSerializerOptions options)
    {
        writer.WritePropertyName(value.ToString());
    }
}

/// <summary>
/// STJ converter for Encoding using WebName (parity with Newtonsoft WebEncodingJsonConverter)
/// </summary>
public sealed class EncodingStjConverter : JsonConverter<Encoding>
{
    public override Encoding Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var webName = reader.GetString();
            return Encoding.GetEncoding(webName!);
        }
        throw new JsonException($"Cannot convert {reader.TokenType} to Encoding");
    }

    public override void Write(Utf8JsonWriter writer, Encoding value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.WebName);
    }
}

/// <summary>
/// STJ converter for PlaceHolderString (parity with Newtonsoft PlaceHolderString.JsonConverter)
/// </summary>
public sealed class PlaceHolderStringStjConverter : JsonConverter<PlaceHolderString>
{
    public override PlaceHolderString? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        if (reader.TokenType == JsonTokenType.String)
        {
            var pattern = reader.GetString();
            return string.IsNullOrEmpty(pattern) ? null : new PlaceHolderString(pattern);
        }

        throw new JsonException($"Cannot convert {reader.TokenType} to PlaceHolderString");
    }

    public override void Write(Utf8JsonWriter writer, PlaceHolderString? value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }
        writer.WriteStringValue(value.Pattern);
    }
}
