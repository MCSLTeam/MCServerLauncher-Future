using System.Text;
using MCServerLauncher.Daemon.Remote.Authentication.PermissionSystem;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace MCServerLauncher.Daemon.Utils;

public static class JsonSettings
{
    public static readonly JsonSerializerSettings Settings = new()
    {
        ContractResolver = new DefaultContractResolver
        {
            NamingStrategy = new SnakeCaseNamingStrategy()
        },
        Converters = new List<JsonConverter>
        {
            new StringEnumConverter(new SnakeCaseNamingStrategy(), allowIntegerValues: false),
            new GuidJsonConverter(),
            new WebEncodingJsonConverter(),
            new Permission.PermissionJsonConverter()
        }
    };

    /// <summary>
    ///     解析 Guid,若字符串解析失败则返回 Guid.Empty,方便带上下文的异常检查
    /// </summary>
    private class GuidJsonConverter : JsonConverter
    {
        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        {
            writer.WriteValue(value!.ToString());
        }

        public override object ReadJson(JsonReader reader, Type objectType, object? existingValue,
            JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.String)
            {
                var str = reader.Value!.ToString();

                return Guid.TryParse(str, out var result) ? result : Guid.Empty;
            }

            throw new JsonSerializationException($"Cannot convert {reader.Value} to Guid");
        }

        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(Guid);
        }
    }

    private class WebEncodingJsonConverter : JsonConverter
    {
        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        {
            var encoding = (Encoding)value!;
            writer.WriteValue(encoding.WebName);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object? existingValue,
            JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.String) return Encoding.GetEncoding(reader.Value!.ToString());
            throw new JsonSerializationException($"Cannot convert {reader.Value} to Encoding");
        }

        public override bool CanConvert(Type objectType)
        {
            return objectType.IsSubclassOf(typeof(Encoding));
        }
    }
}