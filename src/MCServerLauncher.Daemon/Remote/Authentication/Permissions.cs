namespace MCServerLauncher.Daemon.Remote.Authentication;

public sealed class Permissions
{
    public Permissions(string permissions)
        : this(Parse(permissions))
    {
    }

    public Permissions(params string[] permissions)
        : this(Parse(permissions))
    {
    }

    public Permissions(params Permission[] permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        if (permissions.Any(static permission => permission is null))
            throw new ArgumentException("Permission values cannot be null.", nameof(permissions));

        PermissionList = [.. permissions];
    }

    private Permissions()
    {
        PermissionList = [];
    }

    public Permission[] PermissionList { get; }

    public static Permissions Never => new();

    public static bool IsValid(string permissions)
    {
        if (string.IsNullOrEmpty(permissions))
            return false;

        var values = permissions.Split(',');
        return values.Length > 0 && values.All(Permission.IsValid);
    }

    public bool Matches(IMatchable matchable)
    {
        ArgumentNullException.ThrowIfNull(matchable);
        return PermissionList.Any(permission => permission.Matches(matchable));
    }

    public override string ToString() =>
        $"[{string.Join(", ", PermissionList.Select(static permission => permission.ToString()))}]";

    private static Permission[] Parse(string permissions)
    {
        if (!IsValid(permissions))
            throw new ArgumentException("Permissions must be a canonical comma-separated list.", nameof(permissions));

        return permissions.Split(',').Select(Permission.Of).ToArray();
    }

    private static Permission[] Parse(string[] permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        return permissions.Select(Permission.Of).ToArray();
    }
}
