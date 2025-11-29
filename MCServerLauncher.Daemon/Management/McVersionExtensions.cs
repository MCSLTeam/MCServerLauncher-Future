using MCServerLauncher.Daemon.Management.Minecraft;

namespace MCServerLauncher.Daemon.Management;

public static class McVersionExtensions
{
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
        return version.Between(McVersion.Of(min), McVersion.Of(max));
    }
}