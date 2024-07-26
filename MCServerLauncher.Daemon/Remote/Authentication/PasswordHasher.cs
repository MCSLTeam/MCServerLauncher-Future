using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

namespace MCServerLauncher.Daemon.Remote.Authentication;

public static class PasswordHasher
{
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 10000; // 迭代次数
    private static readonly HashAlgorithmName HashAlgorithm = HashAlgorithmName.SHA256;

    /// <summary>
    ///     使用PBKDF2 生成密码哈希，用于密码存储
    /// </summary>
    /// <param name="password"></param>
    /// <returns></returns>
    public static string HashPassword(string password)
    {
        // 生成Salt
        var salt = new byte[SaltSize];
        RandomNumberGenerator.Fill(salt);

        // 使用 PBKDF2 生成哈希
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithm, KeySize);

        // 组合盐值和哈希
        var hashBytes = new byte[SaltSize + KeySize];
        Array.Copy(salt, 0, hashBytes, 0, SaltSize);
        Array.Copy(hash, 0, hashBytes, SaltSize, KeySize);

        // 返回 Base64 字符串表示
        return Convert.ToBase64String(hashBytes);
    }

    /// <summary>
    ///     验证密码
    /// </summary>
    /// <param name="password"></param>
    /// <param name="hashedPassword"></param>
    /// <returns></returns>
    public static bool VerifyPassword(string password, [AllowNull] string hashedPassword)
    {
        if (hashedPassword == null) return false;

        // 解码哈希密码
        var hashBytes = Convert.FromBase64String(hashedPassword);

        // 分离Salt和哈希
        var salt = new byte[SaltSize];
        Array.Copy(hashBytes, 0, salt, 0, SaltSize);

        // 使用相同的盐值和参数重新生成哈希
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithm, KeySize);

        // 比较新生成的哈希和存储的哈希
        for (var i = 0; i < KeySize; i++)
            if (hashBytes[i + SaltSize] != hash[i])
                return false;

        return true;
    }
}