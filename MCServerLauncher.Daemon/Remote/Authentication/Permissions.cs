using System.Text.RegularExpressions;

namespace MCServerLauncher.Daemon.Remote.Authentication;

/// <summary>
///     用户权限组, 用于描述用户的权限集合
/// </summary>
public class Permissions
{
    private static readonly Regex Pattern =
        new(
            @"(?:(?:[a-zA-Z-_]+|\*{1,2})\.)*(?:[a-zA-Z-_]+|\*{1,2})(?:,(?:(?:[a-zA-Z-_]+|\*{1,2})\.)*(?:[a-zA-Z-_]+|\*{1,2}))*");

    public Permissions(string permissions) : this(permissions.Split(','))
    {
    }

    public Permissions(params string[] permissions) : this(permissions.Select(Permission.Of).ToArray())
    {
    }

    public Permissions(params Permission[] permissions)
    {
        PermissionList = permissions;
    }

    private Permissions()
    {
        PermissionList = Array.Empty<Permission>();
    }

    public Permission[] PermissionList { get; }

    public static Permissions Never => new();

    public static bool IsValid(string permissions)
    {
        return Pattern.IsMatch(permissions);
    }

    public bool Matches(IMatchable matchable)
    {
        return PermissionList.Any(matchable.Matches);
    }

    public override string ToString()
    {
        return $"[{string.Join(", ", PermissionList.Select(p => p.ToString()))}]";
    }
}