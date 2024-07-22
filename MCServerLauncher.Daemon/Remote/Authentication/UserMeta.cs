namespace MCServerLauncher.Daemon.Remote.Authentication;

public class UserMeta
{
    public string PasswordHash { get; private set; }
    public PermissionGroups Group { get; private set; }
    public Permission[] Permissions { get; private set; }

    public UserMeta(string passwordHash, PermissionGroups group, Permission[] permissions)
    {
        PasswordHash = passwordHash;
        Group = group;
        Permissions = permissions;
    }

    public override string ToString()
    {
        return "UserMeta(PasswordHash: ******" + ", Group: " + Group + ", Permissions: [...]" + ")";
    }
}

public class User
{
    public string Name { get; private set; }
    public UserMeta Meta { get; private set; }

    public User(string name, UserMeta meta)
    {
        Name = name;
        Meta = meta;
    }

    public override string ToString()
    {
        return "User(Name: " + Name + ", Meta: " + Meta + ")";
    }
}