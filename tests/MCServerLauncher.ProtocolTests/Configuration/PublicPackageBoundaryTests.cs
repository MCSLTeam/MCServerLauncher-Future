using System.Reflection;
using System.Xml.Linq;
using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.DaemonClient;
using DaemonClientFacade = MCServerLauncher.DaemonClient.DaemonClient;

namespace MCServerLauncher.ProtocolTests;

/// <summary>
/// Executable form of the product split rule: the public product must build and ship without any
/// private control plane, node agent or cloud project. These checks read project files and assembly
/// references, so a directory rename cannot satisfy them by accident.
/// </summary>
public sealed class PublicPackageBoundaryTests
{
    /// <summary>Assemblies the public product may reference, beyond the framework and pinned NuGet.</summary>
    private static readonly string[] PublicAssemblyNames =
    [
        "MCServerLauncher.Common",
        "MCServerLauncher.Daemon.API",
        "MCServerLauncher.DaemonClient",
        "MCServerLauncher.Daemon",
        "MCServerLauncher.Daemon.Plugin.Sdk",
        "MCServerLauncher.Daemon.Plugin.Generators",
        "MCServerLauncher.TerminalEmulator",
        "MCServerLauncher.WPF"
    ];

    [Fact]
    public void CommonDoesNotReferenceAnyOtherProductAssembly()
    {
        var references = typeof(InstanceReport).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => name is not null && name.StartsWith("MCServerLauncher.", StringComparison.Ordinal))
            .ToArray();

        // Common is the bottom of the product graph. Reaching upward would make the package
        // unusable on its own and would invert the documented dependency direction.
        Assert.Empty(references);
    }

    [Fact]
    public void DaemonApiReferencesCommonAndNothingElseFromTheProduct()
    {
        var references = typeof(IDaemonApplication).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => name is not null && name.StartsWith("MCServerLauncher.", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(new[] { "MCServerLauncher.Common" }, references);
    }

    [Fact]
    public void DaemonClientReferencesOnlyCommonAndDaemonApi()
    {
        var references = typeof(DaemonClientFacade).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => name is not null && name.StartsWith("MCServerLauncher.", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        // The client is a consumer of the contracts, not of the daemon implementation.
        Assert.Equal(
            new[] { "MCServerLauncher.Common", "MCServerLauncher.Daemon.API" },
            references);
    }

    [Fact]
    public void NoProjectReferencesAPathOutsideThisRepository()
    {
        var violations = ProductProjects()
            .SelectMany(path => XDocument.Load(path)
                .Descendants("ProjectReference")
                .Select(element => new
                {
                    Project = Relative(path),
                    Include = element.Attribute("Include")?.Value ?? string.Empty
                }))
            .Where(reference => IsOutsideRepository(reference.Project, reference.Include))
            .Select(reference => $"{reference.Project} -> {reference.Include}")
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void NoProjectReferencesAPrivatePlatformPackage()
    {
        var violations = ProductProjects()
            .SelectMany(path => XDocument.Load(path)
                .Descendants("PackageReference")
                .Select(element => new
                {
                    Project = Relative(path),
                    Include = element.Attribute("Include")?.Value ?? string.Empty
                }))
            .Where(reference => reference.Include.StartsWith("Mcsl.Platform", StringComparison.OrdinalIgnoreCase))
            .Select(reference => $"{reference.Project} -> {reference.Include}")
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void ProductAssembliesDoNotReferencePrivatePlatformAssemblies()
    {
        var assemblies = new[]
        {
            typeof(InstanceReport).Assembly,
            typeof(IDaemonApplication).Assembly,
            typeof(DaemonClientFacade).Assembly
        };

        var violations = assemblies
            .SelectMany(assembly => assembly.GetReferencedAssemblies()
                .Where(reference => reference.Name?.StartsWith("Mcsl.", StringComparison.Ordinal) == true)
                .Select(reference => $"{assembly.GetName().Name} -> {reference.Name}"))
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void PublicAssemblyListCoversEveryProductProject()
    {
        // Guards the checks above: if a new public assembly appears, this test fails so the
        // dependency rules are extended deliberately rather than silently skipped.
        var assemblyNames = ProductProjects()
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}tests{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}tools{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}benchmarks{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(path => XDocument.Load(path).Descendants("AssemblyName").Select(e => e.Value.Trim()).FirstOrDefault())
            .Select(name => name ?? string.Empty)
            .Where(name => name.Length > 0)
            .ToArray();

        var unknown = assemblyNames
            .Where(name => !PublicAssemblyNames.Contains(name, StringComparer.Ordinal))
            .ToArray();

        Assert.Empty(unknown);
    }

    private static IEnumerable<string> ProductProjects()
    {
        var repositoryRoot = RepositoryRoot();
        return new[] { "src", "tests", "generators", "benchmarks", "tools", "samples" }
            .Select(root => Path.Combine(repositoryRoot, root))
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories))
            .Where(path => !HasGeneratedPathSegment(path));
    }

    private static bool IsOutsideRepository(string projectPath, string include)
    {
        if (include.Length == 0 || include.Contains("$(", StringComparison.Ordinal))
            return false;

        var projectDirectory = Path.GetDirectoryName(Path.Combine(RepositoryRoot(), projectPath))!;
        var normalized = include.Replace('\\', Path.DirectorySeparatorChar);
        var resolved = Path.GetFullPath(Path.Combine(projectDirectory, normalized));
        return !resolved.StartsWith(RepositoryRoot() + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static string Relative(string absolutePath) =>
        Path.GetRelativePath(RepositoryRoot(), absolutePath);

    private static bool HasGeneratedPathSegment(string path) =>
        path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => segment is "bin" or "obj");

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MCServerLauncher.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root.");
    }
}
