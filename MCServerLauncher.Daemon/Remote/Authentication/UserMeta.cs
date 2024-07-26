namespace MCServerLauncher.Daemon.Remote.Authentication;

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