using System.Text.RegularExpressions;
using TouchSocket.Core;

namespace MCServerLauncher.Daemon.Remote.Authentication;

public class Permissions
{
    public static readonly Regex Pattern =
        new(
            @"/^(?:(?:[a-zA-Z-_]+|\*{1,2})\.)*(?:[a-zA-Z-_]+|\*{1,2})(?:,(?:(?:[a-zA-Z-_]+|\*{1,2})\.)*(?:[a-zA-Z-_]+|\*{1,2}))*$/gm");

    private readonly Permission[] _permissions;

    public Permissions(string permissions) : this(permissions.Split(','))
    {
    }

    public Permissions(params string[] permissions) : this(permissions.Select(p => new Permission(p)).ToArray())
    {
    }

    public Permissions(params Permission[] permissions)
    {
        _permissions = permissions;
    }

    public bool Matches(IMatchable matchable)
    {
        return _permissions.Any(matchable.Matches);
    }
}