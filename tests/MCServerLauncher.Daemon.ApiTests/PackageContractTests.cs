using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;

namespace MCServerLauncher.Daemon.ApiTests;

public sealed class PackageContractTests
{
    [Fact]
    public async Task PackedArtifactPinsItsAbiDependenciesExactly()
    {
        var repositoryRoot = FindRepositoryRoot();
        var packageOutput = Path.Combine(Path.GetTempPath(), $"mcsl-daemon-api-package-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(packageOutput);
            var projectPath = Path.Combine(
                repositoryRoot,
                "src",
                "MCServerLauncher.Daemon.API",
                "MCServerLauncher.Daemon.API.csproj");

            var packResult = await RunDotNetAsync(
                repositoryRoot,
                "pack",
                projectPath,
                "--configuration",
                "Release",
                "--no-restore",
                "--output",
                packageOutput,
                "/m:1");

            Assert.True(packResult.ExitCode == 0, $"dotnet pack failed:{Environment.NewLine}{packResult.Output}");

            var packagePath = Assert.Single(Directory.GetFiles(packageOutput, "MCServerLauncher.Daemon.API.*.nupkg"));
            using var package = ZipFile.OpenRead(packagePath);

            var nuspecEntry = Assert.Single(package.Entries, entry => entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal));
            using var nuspecStream = nuspecEntry.Open();
            var nuspec = XDocument.Load(nuspecStream);
            var ns = nuspec.Root?.Name.Namespace ?? throw new InvalidOperationException("The packed nuspec had no root element.");

            var dependencies = nuspec
                .Descendants(ns + "dependency")
                .ToDictionary(
                    element => (string?)element.Attribute("id") ?? throw new InvalidOperationException("A dependency had no id."),
                    element => (string?)element.Attribute("version") ?? throw new InvalidOperationException("A dependency had no version."),
                    StringComparer.Ordinal);

            Assert.Equal(3, dependencies.Count);
            Assert.Equal("[1.0.0]", dependencies["MCServerLauncher.Common"]);
            Assert.Equal("[0.10.1]", dependencies["RustyOptions"]);
            Assert.Equal("[10.0.9]", dependencies["Microsoft.Extensions.Logging.Abstractions"]);
            Assert.Contains(package.Entries, entry => entry.FullName == "lib/net10.0/MCServerLauncher.Daemon.API.dll");
            Assert.Contains(package.Entries, entry => entry.FullName == "README.md");
            Assert.DoesNotContain(package.Entries, entry => entry.FullName.EndsWith("MCServerLauncher.Common.dll", StringComparison.Ordinal));
            Assert.Contains(package.Entries, entry => entry.FullName == "buildTransitive/MCServerLauncher.Daemon.API.targets");

            var metadata = nuspec.Descendants(ns + "metadata").Single();
            var license = metadata.Element(ns + "license");
            Assert.Equal("expression", (string?)license?.Attribute("type"));
            Assert.Equal("GPL-3.0-only", license?.Value);
            Assert.Equal("https://github.com/MCSLTeam/MCServerLauncher-Future", (string?)metadata.Element(ns + "projectUrl"));
            Assert.Equal("MCSLTeam", (string?)metadata.Element(ns + "authors"));
        }
        finally
        {
            if (Directory.Exists(packageOutput))
            {
                Directory.Delete(packageOutput, recursive: true);
            }
        }
    }

    [Fact]
    public void RestoreGraphContainsOnlyTheApprovedDaemonApiPackageClosure()
    {
        var assetsPath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "MCServerLauncher.Daemon.API",
            "obj",
            "project.assets.json");
        Assert.True(File.Exists(assetsPath), $"The restore graph was not found at {assetsPath}.");

        using var document = JsonDocument.Parse(File.ReadAllBytes(assetsPath));
        var packages = document.RootElement
            .GetProperty("libraries")
            .EnumerateObject()
            .Where(static entry => entry.Value.GetProperty("type").GetString() == "package")
            .Select(static entry => entry.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                "Microsoft.Extensions.DependencyInjection.Abstractions/10.0.9",
                "Microsoft.Extensions.Logging.Abstractions/10.0.9",
                "RustyOptions/0.10.1"
            ],
            packages);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "MCServerLauncher.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not find the MCServerLauncher repository root.");
    }

    private static async Task<ProcessResult> RunDotNetAsync(string workingDirectory, params string[] arguments)
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

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start dotnet pack.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        return new ProcessResult(
            process.ExitCode,
            string.Concat(await standardOutput, Environment.NewLine, await standardError));
    }

    private sealed record ProcessResult(int ExitCode, string Output);
}
