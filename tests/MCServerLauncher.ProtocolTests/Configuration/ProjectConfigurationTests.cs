using System.Xml.Linq;

namespace MCServerLauncher.ProtocolTests;

public class ProjectConfigurationTests
{
    [Fact]
    public void DaemonProject_KeepsJsonReflectionFallbackEnabled()
    {
        var projectPath = Path.Combine(
            GetRepositoryRoot(),
            "src",
            "MCServerLauncher.Daemon",
            "MCServerLauncher.Daemon.csproj");

        var document = XDocument.Load(projectPath);
        var value = document
            .Descendants("JsonSerializerIsReflectionEnabledByDefault")
            .Select(element => element.Value.Trim())
            .Single();

        Assert.Equal("true", value);
    }

    private static string GetRepositoryRoot()
    {
        var directory = AppContext.BaseDirectory;

        while (directory is not null && !File.Exists(Path.Combine(directory, "MCServerLauncher.sln")))
        {
            directory = Directory.GetParent(directory)?.FullName;
        }

        if (directory is null)
        {
            throw new DirectoryNotFoundException("Repository root not found for daemon project lookup.");
        }

        return directory;
    }
}
