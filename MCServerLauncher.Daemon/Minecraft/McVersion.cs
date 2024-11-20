namespace MCServerLauncher.Daemon.Minecraft;

public readonly record struct McVersion(ushort Major, ushort Minor, ushort Patch)
{
    public override string ToString()
    {
        return $"{Major}.{Minor}.{Patch}";
    }

    // 重载 > 运算符
    public static bool operator >(McVersion left, McVersion right)
    {
        return left.Major > right.Major ||
               (left.Major == right.Major && left.Minor > right.Minor) ||
               (left.Major == right.Major && left.Minor == right.Minor && left.Patch > right.Patch);
    }

    // 重载 >= 运算符
    public static bool operator >=(McVersion left, McVersion right)
    {
        return left > right || left == right;
    }

    // 重载 < 运算符
    public static bool operator <(McVersion left, McVersion right)
    {
        return !(left >= right);
    }

    // 重载 <= 运算符
    public static bool operator <=(McVersion left, McVersion right)
    {
        return left < right || left == right;
    }
}

public static class McVersionExtensions
{
    public static McVersion Of(string version)
    {
        var parts = version.Split('.').Select(ushort.Parse).ToArray();
        return new McVersion(parts[0], parts[1], parts[2]);
    }

    private static bool Is(ushort digital, string pattern)
    {
        return pattern == "*" || digital == ushort.Parse(pattern);
    }

    /// <summary>
    ///     判断版本号是否为指定模版: 例如版本1.16.5在模版1.16.*、1.*.5、1.*.*中
    /// </summary>
    /// <param name="version"></param>
    /// <param name="pattern"></param>
    /// <returns></returns>
    public static bool In(this McVersion version, string pattern)
    {
        var parts = pattern.Trim().Split('.');
        return Is(version.Major, parts[0]) && Is(version.Minor, parts[1]) && Is(version.Patch, parts[2]);
    }

    public static bool Between(this McVersion version, McVersion min, McVersion max)
    {
        return version >= min && version <= max;
    }

    public static bool Between(this McVersion version, string min, string max)
    {
        return version.Between(Of(min), Of(max));
    }
}