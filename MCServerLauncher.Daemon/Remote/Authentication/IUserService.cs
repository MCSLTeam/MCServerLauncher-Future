using System.Diagnostics.CodeAnalysis;

namespace MCServerLauncher.Daemon.Remote.Authentication;

/// <summary>
///    用户服务接口
/// </summary>
public interface IUserService
{
    void AddUser(string name, string password, PermissionGroups group, Permission[] permissions = null);
    void RemoveUser(string name);
    Dictionary<string, UserMeta> GetUsers();
    bool Authenticate([NotNull] string name, string password, [AllowNull] out UserMeta user);
    bool Authenticate([NotNull] string jwt, [AllowNull] out User user);
}