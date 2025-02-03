using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using MCServerLauncher.Daemon.Remote.Authentication.PermissionSystem;
using Microsoft.IdentityModel.Tokens;
using Serilog;

namespace MCServerLauncher.Daemon.Remote.Authentication;

/// <summary>
///     JWT生成和解析验证工具
/// </summary>
public static class JwtUtils
{
    private static string Secret => AppConfig.Get().Secret;

    /// <summary>
    ///     生成Token(验证pwd 防止出现pwd已更改但token依然有效的情况)，使用HmacSha256生成摘要
    /// </summary>
    /// <param name="usr"></param>
    /// <param name="secret"></param>
    /// <param name="expired">过期的秒数</param>
    /// <returns></returns>
    public static string GenerateToken(string permissions, int expired)
    {
        var secretBytes = Encoding.UTF8.GetBytes(Secret);


        var claims = new[]
        {
            new Claim("permissions", permissions)
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
    public static string? ExtractPermissions(string token)
    {
        return new JwtSecurityTokenHandler().ReadJwtToken(token).Claims.FirstOrDefault(c => c.Type == "permissions")
            ?.Value;
    }

    /// <summary>
    ///     解析Token，返回是否成功
    /// </summary>
    /// <param name="secret"></param>
    /// <param name="jwt"></param>
    /// <returns></returns>
    public static bool ValidateToken(string jwt)
    {
        try
        {
            var tokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret)),
                ValidIssuer = "MCServerLauncher.Daemon",
                ValidAudience = "MCServerLauncher.Daemon",
                ClockSkew = TimeSpan.Zero // <==  *** 消除时钟偏差!!! ***
            };
            var claimsPrincipal = new JwtSecurityTokenHandler().ValidateToken(jwt, tokenValidationParameters, out _);
            var permissions = ExtractPermissions(jwt);
            return permissions != null && Permissions.Pattern.IsMatch(permissions);
        }
        catch (Exception e)
        {
            Log.Debug("[Jwt Utils] Error occurred when validating jwt: {0}", e);
            return false;
        }
    }
}