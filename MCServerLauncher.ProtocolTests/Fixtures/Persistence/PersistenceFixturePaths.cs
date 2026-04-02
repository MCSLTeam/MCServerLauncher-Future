namespace MCServerLauncher.ProtocolTests.Fixtures.Persistence;

/// <summary>
/// Persistence fixture root.
/// Contains JSON fixtures for daemon-managed files: config.json, instance configs, event-rule configs.
/// </summary>
public static class PersistenceFixturePaths
{
    public static string FixtureRoot => Path.Combine(GetProtocolTestsRoot(), "Fixtures", "Persistence");

    public static string InstanceConfigDir => Path.Combine(FixtureRoot, "InstanceConfig");
    public static string EventRuleDir => Path.Combine(FixtureRoot, "EventRule");
    public static string ConfigDir => Path.Combine(FixtureRoot, "Config");

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