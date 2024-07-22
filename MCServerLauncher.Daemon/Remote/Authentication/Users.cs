using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using MCServerLauncher.Daemon.FileManagement;

namespace MCServerLauncher.Daemon.Remote.Authentication;

public class UsersException : Exception
{
    public UsersException(string message) : base(message)
    {
    }
}

public static class Users
{
    private static ConcurrentDictionary<string, UserMeta> _userMap;

    /// <summary>
    /// 创建用户,并创建用户目录
    /// </summary>
    /// <param name="name">用户名</param>
    /// <param name="password">密码</param>
    /// <param name="group">权限组</param>
    /// <param name="permissions">权限列表(PermissionGroup.Custom时才有意义)</param>
    /// <exception cref="UsersException"></exception>
    public static void AddUser(string name, string password, PermissionGroups group, Permission[] permissions = null)
    {
        if (_userMap.ContainsKey(name)) throw new UsersException("User already exists");

        _userMap.TryAdd(
            name,
            new UserMeta(PasswordHasher.HashPassword(password), group, permissions ?? Array.Empty<Permission>())
        );

        // make user directory
        Directory.CreateDirectory(Path.Combine(FileManager.Root, "users", name));
    }

    /// <summary>
    /// 删除用户和用户目录
    /// </summary>
    /// <param name="name">用户名</param>
    /// <exception cref="UsersException"></exception>
    public static void RemoveUser(string name)
    {
        if (!_userMap.ContainsKey(name)) throw new UsersException("User does not exist");

        _userMap.TryRemove(name, out _);

        // delete user directory
        Directory.Delete(Path.Combine(FileManager.Root, "users", name), true);
    }

    /// <summary>
    /// 获取所有用户信息的快照
    /// </summary>
    /// <returns></returns>
    public static Dictionary<string, UserMeta> GetUsers()
    {
        return _userMap.ToDictionary(x => x.Key, x => x.Value);
    }

    private static bool VerifyPassword(string name, string password)
    {
        return PasswordHasher.VerifyPassword(password,
            _userMap.TryGetValue(name, out var userMeta) ? userMeta.PasswordHash : null);
    }

    /// <summary>
    /// 用户登录,若登录成功则返回用户信息
    /// </summary>
    /// <param name="name">用户名</param>
    /// <param name="password">密码</param>
    /// <param name="user">用户信息</param>
    /// <returns>是否登录成功</returns>
    public static bool Authenticate([NotNull] string name, string password, [AllowNull] out UserMeta user)
    {
        if (VerifyPassword(name, password))
        {
            user = _userMap[name];
            return true;
        }

        user = null;
        return false;
    }
    
    /// <summary>
    /// 验证JWT,若验证成功则返回用户信息
    /// </summary>
    /// <param name="jwt"></param>
    /// <param name="user"></param>
    /// <returns></returns>
    public static bool Authenticate([NotNull] string jwt, [AllowNull] out User user)
    {

        var (usr, pwd) = JwtUtils.ValidateToken(jwt);
        var rv = Authenticate(usr, pwd, out var userMeta);
        user = rv ? new User(usr, userMeta) : null;
        return rv;
    }

    /// <summary>
    /// 创建包含默认管理员的UserMap,用于创建默认的users.json
    /// </summary>
    /// <returns></returns>
    private static ConcurrentDictionary<string, UserMeta> GetDefault()
    {
        string pwd;
        var defaultUserMap = new ConcurrentDictionary<string, UserMeta>
        {
            ["admin"] = new(PasswordHasher.HashPassword(pwd = Guid.NewGuid().ToString()), PermissionGroups.Admin, null),
        };

        LogHelper.Info($"*** Default user created: admin, password: {pwd} ***");
        return defaultUserMap;
    }

    public static void Init()
    {
        _userMap = FileManager.ReadJsonAndBackupOr("users.json", GetDefault);
    }
}