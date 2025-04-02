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

    public static McVersion Of(string version)
    {
        var parts = version.Split('.').Select(ushort.Parse).ToArray();
        return new McVersion(parts[0], parts[1], parts[2]);
    }
}