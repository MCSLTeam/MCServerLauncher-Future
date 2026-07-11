using System.Security.Cryptography;

namespace MCServerLauncher.ProtocolDocs;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length > 1 || (args.Length == 1 && !string.Equals(args[0], "--check", StringComparison.Ordinal)))
        {
            Console.Error.WriteLine("Usage: MCServerLauncher.ProtocolDocs [--check]");
            return 2;
        }

        var outputPath = Path.Combine(FindRepositoryRoot(), "src", "MCServerLauncher.Daemon", ".Resources", "Docs", "apifox.json");
        var generated = ApifoxProjectGenerator.Generate();
        if (args.Length == 0)
        {
            File.WriteAllBytes(outputPath, generated);
            Console.WriteLine($"Generated {outputPath} ({generated.Length} bytes, {Hash(generated)}).");
            return 0;
        }

        if (File.Exists(outputPath) && File.ReadAllBytes(outputPath).AsSpan().SequenceEqual(generated))
        {
            Console.WriteLine($"Protocol documentation is current: {outputPath} ({Hash(generated)}).");
            return 0;
        }

        var actual = File.Exists(outputPath) ? File.ReadAllBytes(outputPath) : [];
        Console.Error.WriteLine($"Protocol documentation drift: {outputPath}");
        Console.Error.WriteLine($"Expected SHA-256: {Hash(generated)}");
        Console.Error.WriteLine(File.Exists(outputPath)
            ? $"Actual SHA-256:   {Hash(actual)}"
            : "Actual SHA-256:   <missing>");
        return 1;
    }

    private static string FindRepositoryRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "MCServerLauncher.sln")))
                {
                    return directory.FullName;
                }
            }
        }

        throw new DirectoryNotFoundException("Unable to locate the MCServerLauncher-Future repository root.");
    }

    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
