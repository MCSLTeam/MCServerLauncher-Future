using System.Text.Json;
using NewtonsoftJson = Newtonsoft.Json;
using NewtonsoftLinq = Newtonsoft.Json.Linq;

namespace MCServerLauncher.Common.ProtoType.Serialization;

/// <summary>
/// Newtonsoft bridge converter for STJ JsonElement payload buffers.
/// Keeps raw JSON shape stable while shared contracts migrate off JToken.
/// </summary>
public sealed class NewtonsoftJsonElementConverter : NewtonsoftJson.JsonConverter<JsonElement?>
{
    public override void WriteJson(NewtonsoftJson.JsonWriter writer, JsonElement? value, NewtonsoftJson.JsonSerializer serializer)
    {
        if (value is null)
        {
            writer.WriteNull();
            return;
        }

        writer.WriteRawValue(value.Value.GetRawText());
    }

    public override JsonElement? ReadJson(
        NewtonsoftJson.JsonReader reader,
        Type objectType,
        JsonElement? existingValue,
        bool hasExistingValue,
        NewtonsoftJson.JsonSerializer serializer)
    {
        if (reader.TokenType == NewtonsoftJson.JsonToken.Null)
        {
            return null;
        }

        var token = NewtonsoftLinq.JToken.Load(reader);
        using var document = JsonDocument.Parse(token.ToString(NewtonsoftJson.Formatting.None));
        return document.RootElement.Clone();
    }
}
