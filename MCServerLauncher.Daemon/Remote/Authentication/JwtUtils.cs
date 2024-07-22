using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace MCServerLauncher.Daemon.Remote.Authentication;

public class JwtUtils
{
    private static string Secret => Config.Get().Secret;
    private static readonly SymmetricSecurityKey SecurityKey = new(Encoding.UTF8.GetBytes(Secret));
    private static readonly SigningCredentials SigningCredentials = new(SecurityKey, SecurityAlgorithms.HmacSha256);

    /// <summary>
    /// 生成Token(验证pwd 防止出现pwd已更改但token依然有效的情况)
    /// </summary>
    /// <param name="usr"></param>
    /// <param name="pwd"></param>
    /// <param name="expired">过期的秒数</param>
    /// <returns></returns>
    public static string GenerateToken(string usr,string pwd ,int expired = 30)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.Name, usr),
            new Claim(ClaimTypes.NameIdentifier, pwd)
        };

        var token = new JwtSecurityToken(
            issuer: "MCServerLauncher.Daemon",
            audience: "MCServerLauncher.Daemon",
            claims: claims,
            expires: DateTime.UtcNow.AddSeconds(expired),
            signingCredentials: SigningCredentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
    
    /// <summary>
    /// 解析Token,返回用户名。解析失败会抛出异常，返回可为空
    /// </summary>
    /// <param name="token"></param>
    /// <returns></returns>
    public static (string,string) ValidateToken(string token)
    {
        var tokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = SecurityKey,
            ValidIssuer = "MCServerLauncher.Daemon",
            ValidAudience = "MCServerLauncher.Daemon",
            ClockSkew = TimeSpan.Zero // <==  *** 消除时钟偏差!!! ***
        };
        var claimsPrincipal = new JwtSecurityTokenHandler().ValidateToken(token, tokenValidationParameters, out _);
        return (claimsPrincipal.FindFirst(ClaimTypes.Name)?.Value, claimsPrincipal.FindFirst(ClaimTypes.NameIdentifier)?.Value);
    }
}