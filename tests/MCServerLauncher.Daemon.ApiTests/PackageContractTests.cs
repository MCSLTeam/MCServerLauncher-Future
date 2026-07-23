using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;

namespace MCServerLauncher.Daemon.ApiTests;

public sealed class PackageContractTests
{
    private static readonly PinnedPayload[] PinnedPayloads =
    [
        new(
            "MCServerLauncher.Common.2.0.0-preview.2.nupkg",
            "lib/net10.0/MCServerLauncher.Common.dll"),
        new(
            "MCServerLauncher.Daemon.API.2.0.0-preview.2.nupkg",
            "lib/net10.0/MCServerLauncher.Daemon.API.dll"),
        new(
            "MCServerLauncher.Daemon.API.2.0.0-preview.2.nupkg",
            "buildTransitive/MCServerLauncher.Daemon.API.targets"),
        new(
            "MCServerLauncher.Daemon.Plugin.Sdk.2.0.0-preview.2.nupkg",
            "lib/net10.0/MCServerLauncher.Daemon.Plugin.Sdk.dll"),
        new(
            "MCServerLauncher.Daemon.Plugin.Sdk.2.0.0-preview.2.nupkg",
            "analyzers/dotnet/cs/MCServerLauncher.Daemon.Plugin.Generators.dll"),
        new(
            "MCServerLauncher.Daemon.Plugin.Sdk.2.0.0-preview.2.nupkg",
            "analyzers/dotnet/cs/NuGet.Versioning.dll"),
        new(
            "MCServerLauncher.Daemon.Plugin.Sdk.2.0.0-preview.2.nupkg",
            "buildTransitive/MCServerLauncher.Daemon.Plugin.Sdk.props"),
        new(
            "MCServerLauncher.Daemon.Plugin.Sdk.2.0.0-preview.2.nupkg",
            "buildTransitive/MCServerLauncher.Daemon.Plugin.Sdk.targets")
    ];

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
            Assert.Equal("[2.0.0-preview.2]", dependencies["MCServerLauncher.Common"]);
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
    public async Task PackedCommonArtifactCarriesPublicPackageMetadata()
    {
        var repositoryRoot = FindRepositoryRoot();
        var packageOutput = Path.Combine(Path.GetTempPath(), $"mcsl-common-package-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(packageOutput);
            var projectPath = Path.Combine(
                repositoryRoot,
                "src",
                "MCServerLauncher.Common",
                "MCServerLauncher.Common.csproj");

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

            var packagePath = Assert.Single(Directory.GetFiles(packageOutput, "MCServerLauncher.Common.*.nupkg"));
            using var package = ZipFile.OpenRead(packagePath);
            var nuspecEntry = Assert.Single(package.Entries, entry => entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal));
            using var nuspecStream = nuspecEntry.Open();
            var nuspec = XDocument.Load(nuspecStream);
            var ns = nuspec.Root?.Name.Namespace ?? throw new InvalidOperationException("The packed nuspec had no root element.");
            var metadata = nuspec.Descendants(ns + "metadata").Single();

            Assert.Equal("MCServerLauncher.Common", (string?)metadata.Element(ns + "id"));
            Assert.Equal("2.0.0-preview.2", (string?)metadata.Element(ns + "version"));
            Assert.Equal("MCSLTeam", (string?)metadata.Element(ns + "authors"));
            Assert.Equal("https://github.com/MCSLTeam/MCServerLauncher-Future", (string?)metadata.Element(ns + "projectUrl"));
            Assert.Equal("GPL-3.0-only", metadata.Element(ns + "license")?.Value);
            Assert.Contains(package.Entries, entry => entry.FullName == "README.md");
            Assert.Contains(package.Entries, entry => entry.FullName == "lib/net10.0/MCServerLauncher.Common.dll");
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
    public async Task PackedPluginSdkArtifactPinsDaemonApiExactlyAndEmbedsGenerator()
    {
        var repositoryRoot = FindRepositoryRoot();
        var packageOutput = Path.Combine(Path.GetTempPath(), $"mcsl-plugin-sdk-package-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(packageOutput);
            var projectPath = Path.Combine(
                repositoryRoot,
                "src",
                "MCServerLauncher.Daemon.Plugin.Sdk",
                "MCServerLauncher.Daemon.Plugin.Sdk.csproj");

            var packResult = await RunDotNetAsync(
                repositoryRoot,
                "pack",
                projectPath,
                "--configuration",
                "Release",
                "--output",
                packageOutput,
                "/m:1");

            Assert.True(packResult.ExitCode == 0, $"dotnet pack failed:{Environment.NewLine}{packResult.Output}");

            var packagePath = Assert.Single(Directory.GetFiles(packageOutput, "MCServerLauncher.Daemon.Plugin.Sdk.*.nupkg"));
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

            Assert.Equal(2, dependencies.Count);
            Assert.Equal("[2.0.0-preview.2]", dependencies["MCServerLauncher.Daemon.API"]);
            Assert.Equal("[10.0.9]", dependencies["Microsoft.Extensions.DependencyInjection"]);

            var metadata = nuspec.Descendants(ns + "metadata").Single();
            Assert.Equal("MCServerLauncher.Daemon.Plugin.Sdk", (string?)metadata.Element(ns + "id"));
            Assert.Equal("2.0.0-preview.2", (string?)metadata.Element(ns + "version"));
            Assert.Contains(package.Entries, entry => entry.FullName == "lib/net10.0/MCServerLauncher.Daemon.Plugin.Sdk.dll");
            Assert.Contains(package.Entries, entry => entry.FullName == "README.md");
            Assert.Contains(package.Entries, entry => entry.FullName == "buildTransitive/MCServerLauncher.Daemon.Plugin.Sdk.props");
            Assert.Contains(package.Entries, entry => entry.FullName == "buildTransitive/MCServerLauncher.Daemon.Plugin.Sdk.targets");
            Assert.Contains(
                package.Entries,
                entry => entry.FullName == "analyzers/dotnet/cs/MCServerLauncher.Daemon.Plugin.Generators.dll");
            Assert.DoesNotContain(package.Entries, entry => entry.FullName.EndsWith("MCServerLauncher.Daemon.API.dll", StringComparison.Ordinal));
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

    [Fact]
    public async Task PinnedPayloadsIgnoreOrdinaryReleaseBuildState()
    {
        var repositoryRoot = FindRepositoryRoot();
        var root = Path.Combine(Path.GetTempPath(), $"mcsl-pin-repro-{Guid.NewGuid():N}");
        var firstPackages = Path.Combine(root, "packages-a");
        var secondPackages = Path.Combine(root, "packages-b");

        try
        {
            await PackPinnedClosureAsync(
                repositoryRoot,
                firstPackages,
                Path.Combine(root, "pin-build-a"));

            var warmBuild = await RunDotNetAsync(
                repositoryRoot,
                "build",
                Path.Combine(
                    repositoryRoot,
                    "src",
                    "MCServerLauncher.Daemon.Plugin.Sdk",
                    "MCServerLauncher.Daemon.Plugin.Sdk.csproj"),
                "--configuration",
                "Release",
                "/m:1");
            Assert.True(
                warmBuild.ExitCode == 0,
                $"Ordinary Release build failed:{Environment.NewLine}{warmBuild.Output}");

            await PackPinnedClosureAsync(
                repositoryRoot,
                secondPackages,
                Path.Combine(root, "pin-build-b"));

            var firstHashes = ReadPinnedPayloadHashes(firstPackages);
            var secondHashes = ReadPinnedPayloadHashes(secondPackages);
            Assert.Equal(PinnedPayloads.Length, firstHashes.Count);
            Assert.Equal(PinnedPayloads.Length, secondHashes.Count);
            foreach (var payload in PinnedPayloads)
            {
                var key = $"{payload.PackageName}|{payload.EntryName}";
                Assert.True(
                    string.Equals(firstHashes[key], secondHashes[key], StringComparison.Ordinal),
                    $"Pinned payload hash changed for {key}: {firstHashes[key]} != {secondHashes[key]}");
            }
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static async Task PackPinnedClosureAsync(
        string repositoryRoot,
        string packageOutput,
        string pinBuildRoot)
    {
        Directory.CreateDirectory(packageOutput);
        string[] projects =
        [
            Path.Combine(repositoryRoot, "src", "MCServerLauncher.Common", "MCServerLauncher.Common.csproj"),
            Path.Combine(repositoryRoot, "src", "MCServerLauncher.Daemon.API", "MCServerLauncher.Daemon.API.csproj"),
            Path.Combine(
                repositoryRoot,
                "src",
                "MCServerLauncher.Daemon.Plugin.Sdk",
                "MCServerLauncher.Daemon.Plugin.Sdk.csproj")
        ];

        foreach (var project in projects)
        {
            var pack = await RunDotNetAsync(
                repositoryRoot,
                "pack",
                project,
                "--configuration",
                "Release",
                "--output",
                packageOutput,
                "/m:1",
                "-p:MCSL_PIN_PACKAGE_PAYLOAD=true",
                $"-p:MCSLPinBuildRoot={pinBuildRoot}");
            Assert.True(pack.ExitCode == 0, $"Pinned pack failed:{Environment.NewLine}{pack.Output}");
        }

        AssertPinnedGeneratorPayloadMatchesBuildOutput(packageOutput, pinBuildRoot);
    }

    private static void AssertPinnedGeneratorPayloadMatchesBuildOutput(
        string packageOutput,
        string pinBuildRoot)
    {
        var generatorOutput = Path.Combine(
            pinBuildRoot,
            "bin",
            "MCServerLauncher.Daemon.Plugin.Generators",
            "Release",
            "netstandard2.0");
        var packagePath = Path.Combine(packageOutput, "MCServerLauncher.Daemon.Plugin.Sdk.2.0.0-preview.2.nupkg");
        using var package = ZipFile.OpenRead(packagePath);

        AssertPackageEntryMatchesFile(
            package,
            "analyzers/dotnet/cs/MCServerLauncher.Daemon.Plugin.Generators.dll",
            Path.Combine(generatorOutput, "MCServerLauncher.Daemon.Plugin.Generators.dll"));
        AssertPackageEntryMatchesFile(
            package,
            "analyzers/dotnet/cs/NuGet.Versioning.dll",
            Path.Combine(generatorOutput, "NuGet.Versioning.dll"));
    }

    private static void AssertPackageEntryMatchesFile(
        ZipArchive package,
        string entryName,
        string buildOutputPath)
    {
        Assert.True(File.Exists(buildOutputPath), $"Pinned build output was not found at '{buildOutputPath}'.");
        var entry = package.GetEntry(entryName) ??
                    throw new InvalidDataException($"Missing pinned payload '{entryName}' in the SDK package.");
        using var packageStream = entry.Open();
        var packageHash = Convert.ToHexString(SHA256.HashData(packageStream));
        using var buildOutput = File.OpenRead(buildOutputPath);
        var buildHash = Convert.ToHexString(SHA256.HashData(buildOutput));
        Assert.Equal(buildHash, packageHash);
    }

    private static Dictionary<string, string> ReadPinnedPayloadHashes(string packageOutput)
    {
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var payload in PinnedPayloads)
        {
            var packagePath = Path.Combine(packageOutput, payload.PackageName);
            using var package = ZipFile.OpenRead(packagePath);
            var entry = package.GetEntry(payload.EntryName) ??
                        throw new InvalidDataException($"Missing pinned payload '{payload.EntryName}' in '{packagePath}'.");
            using var stream = entry.Open();
            hashes.Add(
                $"{payload.PackageName}|{payload.EntryName}",
                Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant());
        }

        return hashes;
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

    private sealed record PinnedPayload(string PackageName, string EntryName);
}
