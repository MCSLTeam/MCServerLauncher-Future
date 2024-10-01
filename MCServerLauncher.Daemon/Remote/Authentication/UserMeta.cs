namespace MCServerLauncher.Daemon.Remote.Authentication;

/// <summary>
///     用户元信息，除了用户名以外的数据
/// </summary>
public record UserMeta(string PasswordHash, PermissionGroups Group, Permission[] Permissions)
{
    public override string ToString()
    {
        return "UserMeta(PasswordHash: ******" + ", Group: " + Group + ", Permissions: [...]" + ")";
    }
}

/// <summary>
///     包含用户完整的信息：用户名和Meta
/// </summary>
public record User(string Name, UserMeta Meta)
{
    public override string ToString()
    {
        return "User(Name: " + Name + ", Meta: " + Meta + ")";
    }
}