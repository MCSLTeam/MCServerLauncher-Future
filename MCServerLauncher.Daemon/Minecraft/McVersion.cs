using System.Text.Json.Serialization;

namespace MCServerLauncher.Daemon.Minecraft;

public readonly record struct McVersion(ushort Major, ushort Minor, ushort Patch)
{
    [JsonIgnore] public static readonly McVersion Max = new(ushort.MaxValue, ushort.MaxValue, ushort.MaxValue);

    [JsonIgnore] public static readonly McVersion Min = new(0, 0, 0);

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

        return parts.Length switch
        {
            1 => new McVersion(parts[0], 0, 0),
            2 => new McVersion(parts[0], parts[1], 0),
            3 => new McVersion(parts[0], parts[1], parts[2]),
            _ => throw new ArgumentException("Invalid minecraft version format")
        };
    }
}