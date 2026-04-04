using System.Text.Json;

namespace MCServerLauncher.Benchmarks.Infrastructure;

internal static class BenchmarkFixtureLoader
{
    public static string LoadText(string directory, string fileName)
    {
        return File.ReadAllText(Path.Combine(directory, fileName));
    }

    public static JsonElement LoadJson(string directory, string fileName)
    {
        using var document = JsonDocument.Parse(LoadText(directory, fileName));
        return document.RootElement.Clone();
    }

    public static JsonElement ParseElement(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
