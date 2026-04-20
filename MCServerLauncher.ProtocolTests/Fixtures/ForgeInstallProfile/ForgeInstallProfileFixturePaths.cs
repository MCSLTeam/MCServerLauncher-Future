namespace MCServerLauncher.ProtocolTests.Fixtures.ForgeInstallProfile;

/// <summary>
/// Forge install profile fixture root.
/// Contains JSON fixtures for Forge/NéoForge install profile deserialization tests.
/// </summary>
public static class ForgeInstallProfileFixturePaths
{
    public static string FixtureRoot => Path.Combine(GetProtocolTestsRoot(), "Resources", "ForgeJson");

    public static string Forge152Dir => Path.Combine(FixtureRoot, "forge-1.5.2-7.8.0.684_1.5.2");
    public static string Forge1710Dir => Path.Combine(FixtureRoot, "forge-1.7.10-10.13.0.1150_1.7.10");
    public static string Forge1122Dir => Path.Combine(FixtureRoot, "forge-1.12.2-14.23.5.2861_1.12.2");
    public static string Forge1165Dir => Path.Combine(FixtureRoot, "forge-1.16.5-36.2.42_1.16.5");
    public static string Forge1214Dir => Path.Combine(FixtureRoot, "forge-1.21.4-54.1.16_1.21.4");
    public static string NeoForge211227Dir => Path.Combine(FixtureRoot, "neoforge-21.1.227_1.21.1");
    public static string Cleanroom058Dir => Path.Combine(FixtureRoot, "cleanroom-0.5.8-alpha_1.12.2");

    private static string GetProtocolTestsRoot()
    {
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "MCServerLauncher.ProtocolTests.csproj")))
        {
            dir = Directory.GetParent(dir)?.FullName;
        }
        return dir ?? throw new DirectoryNotFoundException("ProtocolTests root not found");
    }
}
