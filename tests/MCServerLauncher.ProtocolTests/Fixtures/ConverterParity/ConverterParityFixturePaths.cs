namespace MCServerLauncher.ProtocolTests.Fixtures.ConverterParity;

/// <summary>
/// Converter parity fixture root.
/// Contains JSON fixtures for edge cases: Guid, Encoding, PlaceHolderString, Permission, enum formatting.
/// </summary>
public static class ConverterParityFixturePaths
{
    public static string FixtureRoot => Path.Combine(GetProtocolTestsRoot(), "Fixtures", "ConverterParity");

    public static string GuidDir => Path.Combine(FixtureRoot, "Guid");
    public static string EncodingDir => Path.Combine(FixtureRoot, "Encoding");
    public static string PlaceHolderStringDir => Path.Combine(FixtureRoot, "PlaceHolderString");
    public static string PermissionDir => Path.Combine(FixtureRoot, "Permission");
    public static string EnumDir => Path.Combine(FixtureRoot, "Enum");

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