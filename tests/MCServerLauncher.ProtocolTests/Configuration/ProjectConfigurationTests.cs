using System.Xml.Linq;
using System.Text.RegularExpressions;
using MCServerLauncher.Daemon.API.Application;

namespace MCServerLauncher.ProtocolTests;

public class ProjectConfigurationTests
{
    [Fact]
    public void MessagePipeDependency_IsPinnedOnlyToTheDaemonAndDoesNotReachDaemonApi()
    {
        var repositoryRoot = GetRepositoryRoot();
        var projectRoots = new[] { "src", "tests", "generators", "benchmarks", "tools", "samples" };
        var references = projectRoots
            .Select(root => Path.Combine(repositoryRoot, root))
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories))
            .Where(path => !HasGeneratedPathSegment(path))
            .SelectMany(path => XDocument.Load(path)
                .Descendants("PackageReference")
                .Where(element => string.Equals(
                    element.Attribute("Include")?.Value,
                    "MessagePipe",
                    StringComparison.OrdinalIgnoreCase))
                .Select(element => new
                {
                    ProjectPath = Path.GetFullPath(path),
                    Version = element.Attribute("Version")?.Value ?? element.Element("Version")?.Value
                }))
            .ToArray();

        var reference = Assert.Single(references);
        Assert.Equal(
            Path.GetFullPath(Path.Combine(
                repositoryRoot,
                "src",
                "MCServerLauncher.Daemon",
                "MCServerLauncher.Daemon.csproj")),
            reference.ProjectPath);
        Assert.Equal("1.8.2", reference.Version);

        var apiReferences = typeof(IDaemonApplication).Assembly.GetReferencedAssemblies();
        Assert.DoesNotContain(apiReferences, reference =>
            string.Equals(reference.Name, "MessagePipe", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DaemonMessagePipeConfiguration_UsesTheFrozenKeylessAsyncProfile()
    {
        var repositoryRoot = GetRepositoryRoot();
        var compositionSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "MCServerLauncher.Daemon",
            "Bootstrap",
            "DaemonServiceComposition.cs"));
        var configuration = Regex.Match(
            compositionSource,
            @"collection\.AddMessagePipe\(options\s*=>\s*\{(?<body>.*?)\}\);",
            RegexOptions.Singleline);

        Assert.True(configuration.Success, "Daemon MessagePipe configuration block was not found.");
        var assignments = Regex.Matches(
                configuration.Groups["body"].Value,
                @"options\.(?<property>\w+)\s*=\s*(?<value>[^;]+);")
            .Select(match => $"{match.Groups["property"].Value}={match.Groups["value"].Value.Trim()}")
            .ToArray();
        Assert.Equal(
        [
            "EnableAutoRegistration=false",
            "DefaultAsyncPublishStrategy=AsyncPublishStrategy.Sequential",
            "InstanceLifetime=InstanceLifetime.Singleton",
            "EnableCaptureStackTrace=false"
        ], assignments);

        var domainEventSource = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "src",
            "MCServerLauncher.Daemon",
            "Application",
            "Events",
            "DomainEvents.cs"));
        var messagePipeInterfaces = Regex.Matches(
                domainEventSource,
                @"\b(?<name>I(?:Async)?(?:Publisher|Subscriber|MessageHandler))<(?<arguments>[^>\r\n]+)>")
            .ToArray();

        Assert.NotEmpty(messagePipeInterfaces);
        Assert.All(messagePipeInterfaces, usage =>
        {
            Assert.Contains(
                usage.Groups["name"].Value,
                new[] { "IAsyncPublisher", "IAsyncSubscriber", "IAsyncMessageHandler" });
            Assert.DoesNotContain(',', usage.Groups["arguments"].Value);
        });
        Assert.DoesNotContain("GlobalMessagePipe", domainEventSource, StringComparison.Ordinal);
        Assert.DoesNotContain("IPublisher<", domainEventSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ISubscriber<", domainEventSource, StringComparison.Ordinal);
        Assert.DoesNotContain("RequestHandler", domainEventSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Buffered", domainEventSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Distributed", domainEventSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Interprocess", domainEventSource, StringComparison.Ordinal);
    }

    [Fact]
    public void DaemonProject_DisablesJsonReflectionFallbackByDefault()
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

        Assert.Equal("false", value);
    }

    [Fact]
    public void V1DeletionGate_CoversLegacyActionFailureType()
    {
        var script = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "tools",
            "VerifyNoV1Runtime.ps1"));
        var legacyType = string.Concat("Action", "Error");

        Assert.Contains($"'\\b{legacyType}\\b'", script, StringComparison.Ordinal);
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

    private static bool HasGeneratedPathSegment(string path)
    {
        var segments = Path.GetFullPath(path)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Any(segment =>
            segment is "bin" or "obj" or "artifacts" or ".omx" or ".codegraph");
    }
}
