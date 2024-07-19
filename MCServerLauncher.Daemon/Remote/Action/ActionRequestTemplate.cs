using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace MCServerLauncher.Daemon.Remote.Action
{
    internal abstract class ActionRequestTemplate
    {
        private static JsonSerializerSettings _jsonSerializerSettings;

        public static JsonSerializerSettings GetJsonSerializerSettings()
        {
            if (_jsonSerializerSettings == null)
            {
                _jsonSerializerSettings = new JsonSerializerSettings()
                {
                    ContractResolver = new DefaultContractResolver()
                    {
                        NamingStrategy = new SnakeCaseNamingStrategy()
                    }
                }; // snake case
                _jsonSerializerSettings.Converters.Add(new SnakeCaseEnumConverter<TokenType>());
                _jsonSerializerSettings.Converters.Add(new GuidJsonConverter());
            }

            return _jsonSerializerSettings;
        }

        public class Empty
        {
        }

        public class FileUploadRequest
        {
            public string Path;
            public long Size;
            public long ChunkSize;
            public string Sha1;
        }

        public class FileUploadChunk
        {
            public string Data;
            public long Offset;
            public Guid FileId;
        }

        public class NewToken
        {
            public TokenType Type;
            public long Seconds;
            public bool[] Permissions;
        }
    }
    
    /// <summary>
    /// Enum 转换器, 使枚举字面值(BigCamelCase)与json(snake_case)互转
    /// </summary>
    /// <typeparam name="T"></typeparam>
    internal class SnakeCaseEnumConverter<T> : JsonConverter where T : struct, Enum
    {
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var pascalCase = value!.ToString();
            var snakeCase = ConvertPascalCaseToSnakeCase(pascalCase);
            writer.WriteValue(snakeCase);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue,
            JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.String)
            {
                var snakeCase = reader.Value!.ToString();
                var pascalCase = ConvertSnakeCaseToPascalCase(snakeCase);
                if (Enum.TryParse(pascalCase, out T result))
                {
                    return result;
                }
            }

            throw new JsonSerializationException($"Cannot convert {reader.Value} to {typeof(T)}");
        }

        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(T);
        }

        private static string ConvertSnakeCaseToPascalCase(string snakeCase)
        {
            return string.Join(string.Empty,
                snakeCase.Split('_').Select(s => char.ToUpperInvariant(s[0]) + s.Substring(1).ToLowerInvariant()));
        }

        private static string ConvertPascalCaseToSnakeCase(string pascalCase)
        {
            return string.Concat(pascalCase.Select((x, i) =>
                i > 0 && char.IsUpper(x) ? "_" + x.ToString().ToLowerInvariant() : x.ToString().ToLowerInvariant()));
        }
    }
    
    /// <summary>
    /// 解析 Guid,若字符串解析失败则返回 Guid.Empty,方便带上下文的异常检查
    /// </summary>
    internal class GuidJsonConverter : JsonConverter
    {
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            writer.WriteValue(value!.ToString());
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue,
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
}