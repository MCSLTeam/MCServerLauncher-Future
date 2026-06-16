using System.Xml.Linq;

namespace MCServerLauncher.ProtocolTests;

public class ProjectConfigurationTests
{
    [Fact]
    public void DaemonProject_KeepsJsonReflectionFallbackEnabled()
    {
        var projectPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "MCServerLauncher.Daemon",
            "MCServerLauncher.Daemon.csproj"));

        var document = XDocument.Load(projectPath);
        var value = document
            .Descendants("JsonSerializerIsReflectionEnabledByDefault")
            .Select(element => element.Value.Trim())
            .Single();

        Assert.Equal("true", value);
    }
}
