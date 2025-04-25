namespace MCServerLauncher.Daemon.Remote.Authentication.PermissionSystem;

internal interface IPermissionAttribute
{
    IMatchable GetPermission();
}

[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public class PermissionAttribute : Attribute, IPermissionAttribute
{
    private readonly IMatchable _matchable;

    /// <summary>
    ///     为Action Handler添加权限
    /// </summary>
    /// <param name="combinator">"Any" | "All" | "Never" | "Always"</param>
    /// <param name="permissions">权限字符串</param>
    /// <exception cref="ArgumentException">组合子不在以上4种时会报错</exception>
    public PermissionAttribute(string combinator, params string[] permissions)
    {
        _matchable = combinator.ToLower() switch
        {
            "any" => IMatchable.Any(permissions.Select(p => new Permission(p))),
            "all" => IMatchable.All(permissions.Select(p => new Permission(p))),
            "never" => IMatchable.Never(),
            "always" => IMatchable.Always(),
            _ => throw new ArgumentException("Invalid combinator")
        };
    }

    public IMatchable GetPermission()
    {
        return _matchable;
    }
}

[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public class SimplePermissionAttribute : Attribute, IPermissionAttribute
{
    private readonly IMatchable _matchable;

    /// <summary>
    ///     为Action Handler添加权限,进行简单的Any组合
    /// </summary>
    /// <param name="permissions">权限字符串</param>
    /// <exception cref="ArgumentException">组合子不在以上4种时会报错</exception>
    public SimplePermissionAttribute(params string[] permissions)
    {
        _matchable = IMatchable.Any(permissions.Select(p => new Permission(p)));
    }

    public IMatchable GetPermission()
    {
        return _matchable;
    }
}