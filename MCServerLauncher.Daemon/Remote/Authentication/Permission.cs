using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

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
}

/// <summary>
///     STJ converter for Permission (replaces Newtonsoft PermissionJsonConverter)
/// </summary>
public sealed class PermissionStjConverter : JsonConverter<Permission>
{
    public override Permission? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var str = reader.GetString();
            return str is null ? null : Permission.Of(str);
        }

        throw new JsonException($"Cannot convert {reader.TokenType} to Permission");
    }

    public override void Write(Utf8JsonWriter writer, Permission value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
