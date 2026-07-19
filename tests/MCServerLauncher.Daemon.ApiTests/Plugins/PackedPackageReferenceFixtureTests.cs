using System.Diagnostics;

namespace MCServerLauncher.Daemon.ApiTests.Plugins;

public sealed class PackedPackageReferenceFixtureTests
{
    [Fact]
    public async Task PackedPackageReferencePluginPublishOmitsHostProvidedAssemblies()
    {
        var repositoryRoot = FindRepositoryRoot();
        var guide = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "plugin-developer-guide.md"));
        Assert.Contains("-p:MCSLPluginBundle=true", guide, StringComparison.Ordinal);
        var temporaryRoot = Path.Combine(Path.GetTempPath(), $"mcsl-packed-plugin-fixture-{Guid.NewGuid():N}");
        var packageSource = Path.Combine(temporaryRoot, "packages");
        var packageCache = Path.Combine(temporaryRoot, "package-cache");
        var publishDirectory = Path.Combine(temporaryRoot, "publish");

        try
        {
            Directory.CreateDirectory(packageSource);

            await AssertDotNetSucceedsAsync(
                repositoryRoot,
                "pack",
                Path.Combine(repositoryRoot, "src", "MCServerLauncher.Common", "MCServerLauncher.Common.csproj"),
                "--configuration", "Release", "--output", packageSource, "/m:1");
            await AssertDotNetSucceedsAsync(
                repositoryRoot,
                "pack",
                Path.Combine(repositoryRoot, "src", "MCServerLauncher.Daemon.API", "MCServerLauncher.Daemon.API.csproj"),
                "--configuration", "Release", "--output", packageSource, "/m:1");

            await AssertDotNetSucceedsAsync(
                repositoryRoot,
                "publish",
                Path.Combine(repositoryRoot, "tests", "Fixtures", "Plugins", "PackageReferenceConsumer", "PackageReferenceConsumer.csproj"),
                "--configuration", "Release", "--output", publishDirectory,
                "--packages", packageCache,
                $"/p:RestoreAdditionalProjectSources={packageSource}",
                "/p:MCSLPluginBundle=true",
                "/m:1");

            var publishedNames = Directory
                .EnumerateFiles(publishDirectory)
                .Select(Path.GetFileName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            Assert.DoesNotContain("MCServerLauncher.Daemon.API.dll", publishedNames);
            Assert.DoesNotContain("MCServerLauncher.Common.dll", publishedNames);
            Assert.DoesNotContain("RustyOptions.dll", publishedNames);
            Assert.DoesNotContain("Microsoft.Extensions.Logging.Abstractions.dll", publishedNames);
        }
        finally
        {
            if (Directory.Exists(temporaryRoot))
            {
                Directory.Delete(temporaryRoot, recursive: true);
            }
        }
    }

    private static async Task AssertDotNetSucceedsAsync(string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start dotnet.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        Assert.True(
            process.ExitCode == 0,
            $"dotnet {string.Join(' ', arguments)} failed:{Environment.NewLine}{await output}{Environment.NewLine}{await error}");
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "MCServerLauncher.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not find the MCServerLauncher repository root.");
    }
}
