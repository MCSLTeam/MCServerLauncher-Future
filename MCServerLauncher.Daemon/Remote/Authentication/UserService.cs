using MCServerLauncher.Daemon.Storage;
using Serilog;

namespace MCServerLauncher.Daemon.Remote.Authentication;

public class UsersException : Exception
{
    public UsersException(string message) : base(message)
    {
    }
}

/// <summary>
///     用户服务实现类
/// </summary>
public class UserService : IUserService
{
    private readonly UserDatabase _db;

    public UserService(UserDatabase db)
    {
        _db = db;
        // 启动时检查用户文件
        var task = CheckInternalAdminAsync();
        task.ConfigureAwait(false).GetAwaiter().GetResult();
    }


    /// <summary>
    ///     创建用户,并创建用户目录
    /// </summary>
    /// <param name="name">用户名</param>
    /// <param name="password">密码</param>
    /// <param name="group">权限组</param>
    /// <param name="secret">用户secret(用于生成token)</param>
    /// <param name="permissions">权限列表(PermissionGroup.Custom时才有意义)</param>
    /// <exception cref="UsersException"></exception>
    public async Task AddUserAsync(string name, string password, PermissionGroups group, string? secret = null,
        Permission[]? permissions = null)
    {
        if (await _db.HasUser(name)) throw new UsersException("User already exists");
        var notNullSecret = secret ?? Guid.NewGuid().ToString();

        await _db.AddUser(name, notNullSecret, password, group, permissions ?? Array.Empty<Permission>());

        // make user directory
        Directory.CreateDirectory(Path.Combine(FileManager.Root, "users", name));
    }

    /// <summary>
    ///     删除用户和用户目录
    /// </summary>
    /// <param name="name">用户名</param>
    /// <exception cref="UsersException"></exception>
    public async Task RemoveUserAsync(string name)
    {
        if (await _db.HasUser(name))
            await _db.RemoveUser(name);
        else throw new UsersException("User not found");

        // delete user directory
        Directory.Delete(Path.Combine(FileManager.Root, "users", name), true);
    }

    /// <summary>
    ///     获取所有用户信息
    /// </summary>
    /// <returns></returns>
    public Task<IDictionary<string, UserMeta>> GetUsersAsync()
    {
        return _db.GetUsers();
    }

    /// <summary>
    ///     获取用户元信息
    /// </summary>
    /// <param name="name"></param>
    /// <returns></returns>
    public async Task<UserMeta?> GetUserMetaAsync(string name)
    {
        var e = await _db.GetUserEntry(name);
        return e?.Meta;
    }

    /// <summary>
    ///     验证Jwt
    /// </summary>
    /// <param name="jwt">jwt字符串</param>
    /// <returns>
    ///     <see cref="User" />
    /// </returns>
    public async Task<User?> AuthenticateAsync(string jwt)
    {
        var usr = JwtUtils.ExtractUsername(jwt);
        var e = await _db.GetUserEntry(usr);

        if (e == null) return null;

        // 不需要验证密码是否一致，因为改过密码后.secret也会改，那么验证自然也就不会通过了
        return JwtUtils.ValidateToken(e.Secret, jwt)
            ? new User(usr!, e.Meta) // 此时usr必不为null,因为数据库中有条目
            : null;
    }

    /// <summary>
    ///     验证用户名密码
    /// </summary>
    /// <param name="name"></param>
    /// <param name="password"></param>
    /// <returns></returns>
    public async Task<bool> AuthenticateAsync(string name, string password)
    {
        var entry = await _db.GetUserEntry(name);

        return entry != null && PasswordHasher.VerifyPassword(password, entry.Meta.PasswordHash);
    }

    /// <summary>
    ///     生成Jwt
    /// </summary>
    /// <param name="name"></param>
    /// <param name="expired"></param>
    /// <returns></returns>
    public async Task<string?> GenerateTokenAsync(string name, int expired)
    {
        var userEntry = await _db.GetUserEntry(name);
        return userEntry != null ? JwtUtils.GenerateToken(name, userEntry.Secret, expired) : null;
    }

    /// <summary>
    ///     修改用户密码
    /// </summary>
    /// <param name="name"></param>
    /// <param name="newPassword"></param>
    public async Task UserChangePassword(string name, string newPassword)
    {
        var hashed = PasswordHasher.HashPassword(newPassword);
        await using var transaction = await _db.BeginTransactionAsync();

        try
        {
            await ExpireUserTokens(name);
            await _db.UpdateUser(name, password: hashed);
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }


    /// <summary>
    ///     检查默认admin用户，若不存在则创建
    /// </summary>
    /// <returns></returns>
    private async Task CheckInternalAdminAsync()
    {
        if (await _db.HasUser("admin")) return;

        var secret = Guid.NewGuid().ToString();
        var pwd = Guid.NewGuid().ToString();
        await AddUserAsync("admin", pwd, PermissionGroups.Admin, secret);

        Log.Information($" [Users] *** Internal user created: admin, password: {pwd} ***");
    }

    /// <summary>
    ///     通过更改用户的secret来使所有已分发的Jwt失效
    /// </summary>
    /// <param name="name"></param>
    /// <returns></returns>
    private async Task<bool> ExpireUserTokens(string name)
    {
        return await _db.UpdateUser(name, Guid.NewGuid().ToString());
    }
}