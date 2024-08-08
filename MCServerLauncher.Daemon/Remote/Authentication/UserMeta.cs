namespace MCServerLauncher.Daemon.Remote.Authentication;

/// <summary>
/// 用户元信息，除了用户名以外的数据
/// </summary>
public class UserMeta
{
    public UserMeta(string passwordHash, PermissionGroups group, Permission[] permissions)
    {
        PasswordHash = passwordHash;
        Group = group;
        Permissions = permissions;
    }

    public string PasswordHash { get; private set; }
    public PermissionGroups Group { get; }
    public Permission[] Permissions { get; private set; }

    public override string ToString()
    {
        return "UserMeta(PasswordHash: ******" + ", Group: " + Group + ", Permissions: [...]" + ")";
    }
}

/// <summary>
/// 包含用户完整的信息：用户名和Meta
/// </summary>
public class User
{
    public User(string name, UserMeta meta)
    {
        Name = name;
        Meta = meta;
    }

    public string Name { get; }
    public UserMeta Meta { get; }

    public override string ToString()
    {
        return "User(Name: " + Name + ", Meta: " + Meta + ")";
    }
}