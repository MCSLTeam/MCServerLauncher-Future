using System.Data.Common;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;

namespace MCServerLauncher.Daemon.Remote.Authentication;

/// <summary>
///     用户数据库,包含两个表: 1.用户表(users), 2.登录信息表(login)
///     用户表: name, secret, passwordHash, group, permissions
///     登录信息表: name, expired
/// </summary>
public class UserDatabase : IDisposable, IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    public readonly string DbName;

    public UserDatabase(string name = "users.db")
    {
        DbName = name;
        // 创建数据库
        _connection = new SqliteConnection($"Data Source={DbName};");
        InitAsync().ConfigureAwait(false).GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    public void Dispose()
    {
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    ///     初始化数据库
    /// </summary>
    public async Task InitAsync()
    {
        await _connection.OpenAsync();

        // 设置增量化 vacuum
        await SetAutoVacuum(1);

        // 创建用户表
        await using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = """
                              CREATE TABLE IF NOT EXISTS users(
                                  "name" TEXT PRIMARY KEY,
                                  "secret" TEXT,
                                  "passwordHash" TEXT,
                                  "group" TEXT,
                                  "permissions" TEXT
                              );
                              """;
            await cmd.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    ///     关闭数据库
    /// </summary>
    public async Task CloseAsync()
    {
        await _connection.CloseAsync();
    }

    /// <summary>
    ///     获取用户信息
    /// </summary>
    /// <param name="name"></param>
    /// <returns></returns>
    public async Task<UsersEntry?> GetUserEntry(string? name)
    {
        if (name == null) return null;

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM users WHERE name = @name";
        cmd.Parameters.AddWithValue("@name", name);
        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync()
            ? new UsersEntry(
                reader.GetString(0),
                reader.GetString(1),
                new UserMeta(
                    reader.GetString(2),
                    (PermissionGroups)Enum.Parse(typeof(PermissionGroups),
                        reader.GetString(3)),
                    JsonConvert.DeserializeObject<Permission[]>(reader.GetString(4))!
                )
            )
            : null;
    }

    /// <summary>
    ///     获取所有用户信息
    /// </summary>
    /// <returns></returns>
    public async Task<IDictionary<string, UserMeta>> GetUsers()
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM users";
        await using var reader = await cmd.ExecuteReaderAsync();
        var users = new Dictionary<string, UserMeta>();
        while (await reader.ReadAsync())
            users.Add(reader.GetString(0), new UserMeta(
                reader.GetString(2),
                (PermissionGroups)Enum.Parse(typeof(PermissionGroups), reader.GetString(3)),
                JsonConvert.DeserializeObject<Permission[]>(reader.GetString(4))!
            ));

        return users;
    }

    /// <summary>
    ///     添加用户
    /// </summary>
    /// <param name="name"></param>
    /// <param name="secret"></param>
    /// <param name="password"></param>
    /// <param name="group"></param>
    /// <param name="permissions"></param>
    /// <returns></returns>
    public async Task<bool> AddUser(string name, string secret, string password, PermissionGroups group,
        Permission[] permissions)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
                          INSERT INTO users(name, secret, passwordHash, "group", permissions)
                          VALUES(@name, @secret, @passwordHash, @group, @permissions)
                          """;
        cmd.Parameters.AddWithValue("@name", name);
        cmd.Parameters.AddWithValue("@secret", secret);
        cmd.Parameters.AddWithValue("@passwordHash", PasswordHasher.HashPassword(password));
        cmd.Parameters.AddWithValue("@group", group.ToString());
        cmd.Parameters.AddWithValue("@permissions", JsonConvert.SerializeObject(permissions));
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    /// <summary>
    ///     更新用户信息,只更新非空参数
    /// </summary>
    /// <param name="name"></param>
    /// <param name="secret"></param>
    /// <param name="password"></param>
    /// <param name="group"></param>
    /// <param name="permissions"></param>
    /// <returns></returns>
    public async Task<bool> UpdateUser(
        string name,
        string? secret = default,
        string? password = default,
        PermissionGroups? group = default,
        Permission[]? permissions = default
    )
    {
        // 构建动态SQL语句
        var setClauses = new List<string>();
        var parameters = new List<(string Name, object Value)>();

        if (secret != null)
        {
            setClauses.Add("\"secret\" = @secret");
            parameters.Add(("@secret", secret));
        }

        if (password != null)
        {
            setClauses.Add("\"passwordHash\" = @passwordHash");
            parameters.Add(("@passwordHash", password));
        }

        if (group.HasValue)
        {
            setClauses.Add("\"group\" = @group");
            parameters.Add(("@group", group.Value.ToString()));
        }

        if (permissions != null && permissions.Length > 0)
        {
            // 假设permissions需要转换成字符串形式存储
            var permissionsStr = JsonConvert.SerializeObject(permissions);
            setClauses.Add("\"permissions\" = @permissions");
            parameters.Add(("@permissions", permissionsStr));
        }

        var sql = "UPDATE users SET " + string.Join(", ", setClauses) + " WHERE \"name\" = @name";
        parameters.Add(("@name", name));

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (param, value) in parameters)
            cmd.Parameters.AddWithValue(param, value);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    /// <summary>
    ///     从数据库移除用户
    /// </summary>
    /// <param name="name"></param>
    public async Task RemoveUser(string name)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM users WHERE name = @name";
        cmd.Parameters.AddWithValue("@name", name);
        await cmd.ExecuteNonQueryAsync();
    }


    /// <summary>
    ///     检查用户是否存在
    /// </summary>
    /// <param name="name"></param>
    /// <returns></returns>
    public async Task<bool> HasUser(string name)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM users WHERE name = @name";
        cmd.Parameters.AddWithValue("@name", name);
        return await cmd.ExecuteScalarAsync() != null;
    }


    /// <summary>
    ///     开启事务
    /// </summary>
    /// <returns></returns>
    public ValueTask<DbTransaction> BeginTransactionAsync()
    {
        return _connection.BeginTransactionAsync();
    }

    // public async Task<LoginEntry?> GetLoginEntry(string name)
    // {
    //     await using var cmd = _connection.CreateCommand();
    //     cmd.CommandText = "SELECT name, expired FROM login WHERE name = @name";
    //     cmd.Parameters.AddWithValue("@name", name);
    //     await using var reader = await cmd.ExecuteReaderAsync();
    //     return await reader.ReadAsync()
    //         ? new LoginEntry(reader.GetString(0), reader.GetInt64(1))
    //         : null;
    // }
    //
    // public async Task RemoveLoginEntry(string name)
    // {
    //     await using var cmd = _connection.CreateCommand();
    //     cmd.CommandText = "DELETE FROM login WHERE name = @name";
    //     cmd.Parameters.AddWithValue("@name", name);
    //     await cmd.ExecuteNonQueryAsync();
    // }
    //
    // public async Task UpdateLoginEntry(string name, long expired)
    // {
    //     await using var cmd = _connection.CreateCommand();
    //     cmd.CommandText = """
    //                       INSERT INTO login(name, expired)
    //                       VALUES(@name, @expired)
    //                       ON CONFLICT(name) DO UPDATE SET
    //                           expired = @expired
    //                       """;
    //     cmd.Parameters.AddWithValue("@name", name);
    //     cmd.Parameters.AddWithValue("@expired", expired);
    //     await cmd.ExecuteNonQueryAsync();
    // }

    /// <summary>
    ///     set auto vacuum mode
    /// </summary>
    /// <param name="mode">
    ///     vacuum mode:
    ///     <list type="bullet">
    ///         <item>0: no auto vacuum</item>
    ///         <item>1: incremental vacuum</item>
    ///         <item>2: full vacuum</item>
    ///     </list>
    /// </param>
    public async Task SetAutoVacuum(int mode)
    {
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = mode switch
        {
            0 => "PRAGMA auto_vacuum = NONE",
            1 => "PRAGMA auto_vacuum = INCREMENTAL",
            2 => "PRAGMA auto_vacuum = FULL",
            _ => throw new ArgumentException("Invalid vacuum mode")
        };
        await cmd.ExecuteNonQueryAsync();
    }

    public record UsersEntry(string Name, string Secret, UserMeta Meta);

    // public record LoginEntry(string Name, long Expired);
}