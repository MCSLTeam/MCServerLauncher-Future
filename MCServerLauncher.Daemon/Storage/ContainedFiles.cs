using System.Reflection;
using Serilog;

namespace MCServerLauncher.Daemon.Storage;

public static class ContainedFiles
{
    public const string NeoForgeServerLauncher = "server.jar"; // Forge, NeoForge 1.17及以上版本的jar启动器, 用于替代run.sh / run.bat

    private static readonly Assembly Assembly = Assembly.GetExecutingAssembly();

    private static readonly string[] Files =
    {
        NeoForgeServerLauncher
    };

    public static void ExtractContained()
    {
        foreach (var name in Files) EnsureContained(name).Wait();
    }

    public static async Task<string> EnsureContained(string contained)
    {
        var target = Path.Combine(FileManager.ContainedRoot, contained);
        if (File.Exists(target)) return target;

        await using var stream = Assembly.GetManifestResourceStream($"MCServerLauncher.Daemon.Contained.{contained}");
        if (stream == null) throw new FileNotFoundException($"Contained file not found: {contained}");
        await using var file = File.Create(target);
        await stream.CopyToAsync(file);
        Log.Debug($"[ContainedFiles]Extracted contained file: {target}");
        return target;
    }
}