namespace MCServerLauncher.Daemon.Remote.Authentication;

/// <summary>
///     用户服务接口
/// </summary>
public interface IUserService
{
    Task AddUserAsync(string name, string password, PermissionGroups group, string? secret = null,
        Permission[]? permissions = null);

    Task RemoveUserAsync(string name);
    Task<IDictionary<string, UserMeta>> GetUsersAsync();
    Task<UserMeta?> GetUserMetaAsync(string name);
    Task<bool> AuthenticateAsync(string name, string password);
    Task<User?> AuthenticateAsync(string jwt);

    Task<string?> GenerateTokenAsync(string name, int expired);
}