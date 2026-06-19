namespace MCServerLauncher.ProtocolTests.Helpers.Integration;

/// <summary>
/// Integration helper root.
/// Contains shared helpers for daemon ↔ daemonclient integration testing.
/// </summary>
public static class IntegrationHelperPaths
{
    public static string HelperRoot => Path.Combine(GetProtocolTestsRoot(), "Helpers", "Integration");

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