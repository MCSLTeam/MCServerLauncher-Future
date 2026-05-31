using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using MCServerLauncher.Common.ProtoType;

namespace MCServerLauncher.Common.ProtoType.Serialization;

/// <summary>
///     Newtonsoft JsonSerializerSettings retained for characterization tests only.
///     Production code uses StjResolver.CreateDefaultOptions().
/// </summary>
public static class JsonSettings
{
    public static readonly JsonSerializerSettings Settings = new()
    {
        ContractResolver = new DefaultContractResolver
        {
            NamingStrategy = new SnakeCaseNamingStrategy()
        },
        Converters =
        {
            new StringEnumConverter(new SnakeCaseNamingStrategy(), false),
            new GuidJsonConverter(),
            new WebEncodingJsonConverter(),
            new PlaceHolderStringNewtonsoftConverter()
        }
    };

    private sealed class GuidJsonConverter : JsonConverter<Guid>
    {
        public override Guid ReadJson(JsonReader reader, Type objectType, Guid existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType != JsonToken.String) return Guid.Empty;
            return Guid.TryParse(reader.Value?.ToString(), out var result) ? result : Guid.Empty;
        }

        public override void WriteJson(JsonWriter writer, Guid value, JsonSerializer serializer)
            => writer.WriteValue(value.ToString());
    }

    private sealed class WebEncodingJsonConverter : JsonConverter<Encoding>
    {
        public override Encoding? ReadJson(JsonReader reader, Type objectType, Encoding? existingValue, bool hasExistingValue, JsonSerializer serializer)
            => reader.Value is string s ? Encoding.GetEncoding(s) : existingValue;

        public override void WriteJson(JsonWriter writer, Encoding? value, JsonSerializer serializer)
            => writer.WriteValue(value?.WebName);
    }

    private sealed class PlaceHolderStringNewtonsoftConverter : JsonConverter<PlaceHolderString>
    {
        public override void WriteJson(JsonWriter writer, PlaceHolderString? value, JsonSerializer serializer)
        {
            if (value == null) { writer.WriteNull(); return; }
            writer.WriteValue(value.Pattern);
        }

        public override PlaceHolderString? ReadJson(JsonReader reader, Type objectType, PlaceHolderString? existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            var pattern = reader.Value?.ToString();
            return string.IsNullOrEmpty(pattern) ? null : new PlaceHolderString(pattern!);
        }
    }
}
