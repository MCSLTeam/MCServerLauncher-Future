namespace MCServerLauncher.Benchmarks.Infrastructure;

internal static class BenchmarkFixturePaths
{
    private static readonly Lazy<string> RepoRoot = new(FindRepoRoot);

    public static string ActionRequestDir => Path.Combine(RepoRoot.Value, "MCServerLauncher.ProtocolTests", "Fixtures", "Rpc", "ActionRequest");
    public static string ActionResponseDir => Path.Combine(RepoRoot.Value, "MCServerLauncher.ProtocolTests", "Fixtures", "Rpc", "ActionResponse");
    public static string EventPacketDir => Path.Combine(RepoRoot.Value, "MCServerLauncher.ProtocolTests", "Fixtures", "Rpc", "EventPacket");

    private static string FindRepoRoot()
    {
        var dir = AppDomain.CurrentDomain.BaseDirectory;

        while (dir is not null && !File.Exists(Path.Combine(dir, "MCServerLauncher.sln")))
        {
            dir = Directory.GetParent(dir)?.FullName;
        }

        return dir ?? throw new DirectoryNotFoundException("Repository root not found for benchmark fixtures.");
    }
}
