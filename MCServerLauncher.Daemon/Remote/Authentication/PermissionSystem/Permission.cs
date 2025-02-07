using System.Text.RegularExpressions;

namespace MCServerLauncher.Daemon.Remote.Authentication.PermissionSystem;

public class Permission : IMatchable
{
    private static readonly Regex Pattern = new(@"/^(([a-zA-Z-_]+|\*{1,2})\.)*([a-zA-Z-_]+|\*{1,2})$/");

    private readonly string _permission;

    public Permission(string permission)
    {
        if (IsValid(permission))
            throw new ArgumentException("Invalid permission");
        _permission = permission;
    }

    public bool Matches(IMatchable p)
    {
        if (p is Permission permission)
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

    public static bool IsValid(string permissions)
    {
        return Pattern.IsMatch(permissions);
    }

    public override string ToString()
    {
        return _permission;
    }
}