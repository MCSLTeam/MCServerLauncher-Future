using System.Text.RegularExpressions;

namespace MCServerLauncher.Daemon.Remote.Authentication.PermissionSystem;

public class Permissions
{
    private static readonly Regex Pattern =
        new(
            @"/^(?:(?:[a-zA-Z-_]+|\*{1,2})\.)*(?:[a-zA-Z-_]+|\*{1,2})(?:,(?:(?:[a-zA-Z-_]+|\*{1,2})\.)*(?:[a-zA-Z-_]+|\*{1,2}))*$/gm");

    public Permissions(string permissions) : this(permissions.Split(','))
    {
    }

    public Permissions(params string[] permissions) : this(permissions.Select(p => new Permission(p)).ToArray())
    {
    }

    public Permissions(params Permission[] permissions)
    {
        PermissionList = permissions;
    }

    public Permission[] PermissionList { get; }

    public static Permissions Never => new("");

    public static bool IsValid(string permissions)
    {
        return Pattern.IsMatch(permissions);
    }

    public bool Matches(IMatchable matchable)
    {
        return PermissionList.Any(matchable.Matches);
    }
}