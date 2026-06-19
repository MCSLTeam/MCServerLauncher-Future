namespace MCServerLauncher.ProtocolTests.Fixtures.Rpc;

/// <summary>
/// RPC wire fixture root.
/// Contains golden JSON fixtures for ActionRequest, ActionResponse, EventPacket, and representative payloads.
/// </summary>
public static class RpcFixturePaths
{
    public static string FixtureRoot => Path.Combine(GetProtocolTestsRoot(), "Fixtures", "Rpc");

    public static string ActionRequestDir => Path.Combine(FixtureRoot, "ActionRequest");
    public static string ActionResponseDir => Path.Combine(FixtureRoot, "ActionResponse");
    public static string EventPacketDir => Path.Combine(FixtureRoot, "EventPacket");

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