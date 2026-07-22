using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Serilog;

namespace MCServerLauncher.Daemon.Remote.Authentication;

/// <summary>
///     JWT生成和解析验证工具
/// </summary>
public static class JwtUtils
{
    public const int DefaultExpires = 360;

    private static string Secret => AppConfig.Get().Secret;

    /// <summary>
    ///     生成Token(验证pwd 防止出现pwd已更改但token依然有效的情况)，使用HmacSha256生成摘要
    /// </summary>
    /// <param name="permissions">权限组表达式字符串<see cref="Permissions" /></param>
    /// <param name="expired">过期的秒数</param>
    /// <returns></returns>
    public static string GenerateToken(string permissions, int expired)
    {
        var secretBytes = Encoding.UTF8.GetBytes(Secret);

        var tokenId = Guid.NewGuid().ToString("N");

        var claims = new[]
        {
            new Claim("permissions", permissions),
            new Claim(JwtRegisteredClaimNames.Jti, tokenId),
            new Claim(JwtRegisteredClaimNames.Sub, $"token:{tokenId}")
        };

        var token = new JwtSecurityToken(
            "MCServerLauncher.Daemon",
            "MCServerLauncher.Daemon",
            claims,
            expires: DateTime.UtcNow.AddSeconds(expired),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(secretBytes),
                SecurityAlgorithms.HmacSha256
            )
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    ///     从Token中提取权限
    /// </summary>
    /// <param name="token"></param>
    /// <returns></returns>
    public static (Guid JTI, string Subject, string? Permissions, DateTime ValidTo) ReadToken(string token)
    {
        if (token == AppConfig.Get().MainToken)
            return (Guid.Empty, "daemon-main", "*", DateTime.MaxValue);

        var parsed = new JwtSecurityTokenHandler().ReadJwtToken(token);
        var permissions = parsed.Claims.FirstOrDefault(c => c.Type == "permissions")?.Value;
        var expires = parsed.ValidTo;
        var id = string.IsNullOrWhiteSpace(parsed.Id)
            ? throw new FormatException("The JWT jti claim is required.")
            : parsed.Id;
        var subject = string.IsNullOrWhiteSpace(parsed.Subject)
            ? throw new FormatException("The JWT sub claim is required.")
            : parsed.Subject;
        return (Guid.Parse(id), subject, permissions, expires);
    }

    /// <summary>
    ///     解析Token，返回是否成功
    /// </summary>
    /// <param name="jwt"></param>
    /// <returns></returns>
    public static bool ValidateToken(string jwt)
    {
        // 如果是主令牌，则直接返回true
        if (jwt == AppConfig.Get().MainToken) return true;

        try
        {
            var config = AppConfig.Get();
            var apiCanonical = string.IsNullOrWhiteSpace(config.Security.ApiCanonicalUri)
                ? "mcsl://daemon/api/v2"
                : config.Security.ApiCanonicalUri;
            var tokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret)),
                ValidIssuer = "MCServerLauncher.Daemon",
                // Preview-1 dual-accept: resource audience + legacy audience.
                ValidAudiences = new[] { "MCServerLauncher.Daemon", apiCanonical },
                ClockSkew = TimeSpan.Zero // <==  *** 消除时钟偏差!!! ***
            };
            var claimsPrincipal = new JwtSecurityTokenHandler().ValidateToken(jwt, tokenValidationParameters, out _);
            var (_, _, permissions, _) = ReadToken(jwt);
            return permissions != null && Permissions.IsValid(permissions);
        }
        catch (Exception e)
        {
            Log.Verbose("[Jwt Utils] Can't validate jwt: {0}", e.Message);
            return false;
        }
    }
}
