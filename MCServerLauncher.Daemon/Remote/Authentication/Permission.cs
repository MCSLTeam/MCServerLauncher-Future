using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace MCServerLauncher.Daemon.Remote.Authentication;

/// <summary>
///     用户权限, 用于判断用户是否有权限执行某个操作
/// </summary>
public class Permission : IMatchable
{
    private static readonly Regex Pattern = new(@"(?:(?:[a-zA-Z-_]+|\*{1,2})\.)*(?:[a-zA-Z-_]+|\*{1,2})");

    private readonly string _permission;

    private Permission(string permission)
    {
        if (!IsValid(permission))
            throw new ArgumentException("Invalid permission");
        _permission = permission;
    }

    public bool Matches(IMatchable matchable)
    {
        if (matchable is Permission permission)
        {
            var pattern = permission._permission
                .Replace(".", "\\s")
                .Replace("**", ".+")
                .Replace("*", "\\S+");
            pattern = "^" + pattern + "(\\s.+)?$";

            var input = _permission.Replace(".", " ");

            return Regex.IsMatch(input, pattern);
        }

        return false;
    }

    public static Permission Of(string permission)
    {
        return new Permission(permission);
    }

    public static bool IsValid(string permission)
    {
        return Pattern.IsMatch(permission);
    }

    public override string ToString()
    {
        return _permission;
    }

    public class PermissionJsonConverter : JsonConverter
    {
        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        {
            var permission = (Permission)value!;
            writer.WriteValue(permission.ToString());
        }

        public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue,
            JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.String)
            {
                var str = reader.Value?.ToString();
                return str == null ? null : Of(str);
            }

            throw new JsonSerializationException($"Cannot convert {reader.Value} to <class: Permission>");
        }

        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(Permission);
        }
    }
}