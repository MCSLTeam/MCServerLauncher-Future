using System.Text.Json;
using System.Text.Json.Serialization;

namespace MCServerLauncher.Daemon.Remote.Authentication;

public sealed class Permission : IMatchable
{
    private readonly string _permission;
    private readonly string[] _segments;
    private readonly bool _matchesAll;

    private Permission(string permission)
    {
        if (!TryParse(permission, out var segments, out var matchesAll))
            throw new ArgumentException("Permission must be a canonical segment pattern.", nameof(permission));

        _permission = permission;
        _segments = segments;
        _matchesAll = matchesAll;
    }

    public bool Matches(IMatchable matchable) =>
        matchable is Permission required && Covers(required);

    public static Permission Of(string permission) => new(permission);

    public static bool IsValid(string permission) =>
        TryParse(permission, out _, out _);

    public override string ToString() => _permission;

    private bool Covers(Permission required)
    {
        if (_matchesAll)
            return true;
        if (required._matchesAll)
            return false;

        var grantIndex = 0;
        var requiredIndex = 0;
        while (grantIndex < _segments.Length)
        {
            var grant = _segments[grantIndex];
            if (grant == "**")
                return requiredIndex < required._segments.Length;
            if (requiredIndex >= required._segments.Length)
                return false;

            var requested = required._segments[requiredIndex];
            if (requested == "**")
                return false;
            if (requested == "*" && grant != "*")
                return false;
            if (grant != "*" && !string.Equals(grant, requested, StringComparison.Ordinal))
                return false;

            grantIndex++;
            requiredIndex++;
        }

        return requiredIndex == required._segments.Length;
    }

    private static bool TryParse(
        string? permission,
        out string[] segments,
        out bool matchesAll)
    {
        segments = [];
        matchesAll = false;
        if (string.IsNullOrEmpty(permission))
            return false;

        if (permission == "*")
        {
            segments = [permission];
            matchesAll = true;
            return true;
        }

        var parsed = permission.Split('.');
        for (var index = 0; index < parsed.Length; index++)
        {
            var segment = parsed[index];
            if (segment.Length == 0)
                return false;
            if (segment == "*")
                continue;
            if (segment == "**")
            {
                if (index != parsed.Length - 1)
                    return false;
                continue;
            }

            if (!IsCanonicalSegment(segment))
                return false;
        }

        segments = parsed;
        return true;
    }

    private static bool IsCanonicalSegment(string segment)
    {
        if (!IsLowerAsciiLetterOrDigit(segment[0]) ||
            !IsLowerAsciiLetterOrDigit(segment[^1]))
        {
            return false;
        }

        foreach (var character in segment)
        {
            if (!IsLowerAsciiLetterOrDigit(character) && character is not ('-' or '_'))
                return false;
        }

        return true;
    }

    private static bool IsLowerAsciiLetterOrDigit(char character) =>
        character is >= 'a' and <= 'z' or >= '0' and <= '9';
    public sealed class PermissionJsonConverter : JsonConverter<Permission>
    {
        public override Permission? Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                var value = reader.GetString();
                return value is null ? null : Of(value);
            }

            throw new JsonException($"Cannot convert {reader.TokenType} to Permission");
        }

        public override void Write(
            Utf8JsonWriter writer,
            Permission value,
            JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString());
    }
}
