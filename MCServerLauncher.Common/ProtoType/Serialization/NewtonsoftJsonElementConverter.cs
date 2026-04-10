using System.Text.Json;
using NewtonsoftJson = Newtonsoft.Json;
using NewtonsoftLinq = Newtonsoft.Json.Linq;

namespace MCServerLauncher.Common.ProtoType.Serialization;

/// <summary>
/// Compatibility bridge converter for Newtonsoft → STJ JsonElement interop.
/// Bounded to RPC parameter paths (ActionRequest.Parameter, ActionResponse.Data)
/// where Newtonsoft serialization is still used for deserialization.
/// STJ handles JsonElement natively on the canonical wire path; this converter
/// exists only to keep Newtonsoft deserialization producing correct JsonElement values.
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
