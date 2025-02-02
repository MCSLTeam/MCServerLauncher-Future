using System.Text.RegularExpressions;

namespace MCServerLauncher.Daemon.Remote.Authentication;

public class Permission : IMatchable
{
    public static readonly Regex Pattern = new(@"/^(([a-zA-Z-_]+|\*{1,2})\.)*([a-zA-Z-_]+|\*{1,2})$/");

    private readonly string _permission;

    public Permission(string permission)
    {
        if (Pattern.IsMatch(permission))
            throw new ArgumentException("Invalid permission");
        _permission = permission;
    }

    public bool Matches(Permission p)
    {
        string pattern = _permission
            .Replace(".", "\\s")
            .Replace("**", ".+")
            .Replace("*", "\\S+");
        pattern = "^" + pattern + "(\\s.+)?$";

        string input = p.ToString().Replace(".", " ");

        return Regex.IsMatch(input, pattern);
    }

    public override string ToString()
    {
        return _permission;
    }
}