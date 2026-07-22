using System.Diagnostics;
using System.Security;
using MCServerLauncher.Daemon.API.Plugins;

namespace MCServerLauncher.Daemon.ApiTests.Plugins;

public sealed class ExternalCompileFixtureTests
{
    [Fact]
    public async Task ExternalFixtureCompilesAgainstOnlyPublicSdkContracts()
    {
        var repositoryRoot = FindRepositoryRoot();
        var projectPath = Path.Combine(
            repositoryRoot,
            "tests",
            "Fixtures",
            "Plugins",
            "ExternalCompile",
            "ExternalCompile.csproj");

        var build = await BuildAsync(projectPath, repositoryRoot);

        Assert.True(
            build.ExitCode == 0,
            $"The external plugin fixture did not compile:{Environment.NewLine}{build.Output}");
    }

    [Fact]
    public async Task ExternalCodeCannotConstructCloneOrMutateVerifiedPrincipal()
    {
        var repositoryRoot = FindRepositoryRoot();
        var apiProject = Path.Combine(
            repositoryRoot,
            "src",
            "MCServerLauncher.Daemon.API",
            "MCServerLauncher.Daemon.API.csproj");
        var root = Directory.CreateTempSubdirectory("mcsl-verified-principal-probe-").FullName;
        try
        {
            var projectPath = Path.Combine(root, "ExternalVerifiedPrincipalProbe.csproj");
            var escapedApiProject = SecurityElement.Escape(apiProject)
                ?? throw new InvalidOperationException("Could not escape the Daemon API project path.");
            await File.WriteAllTextAsync(projectPath, $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <LangVersion>14</LangVersion>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <Nullable>enable</Nullable>
                    <AssemblyName>ExternalVerifiedPrincipalProbe</AssemblyName>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="{{escapedApiProject}}" />
                  </ItemGroup>
                </Project>
                """);
            await File.WriteAllTextAsync(Path.Combine(root, "PrincipalProbe.cs"), """
                using System.Collections.Immutable;
                using MCServerLauncher.Daemon.API.Plugins;

                public static class PrincipalProbe
                {
                    public static VerifiedPrincipal Construct() => new(
                        "external-user",
                        "token-id",
                        "issuer",
                        "audience",
                        DateTimeOffset.MaxValue,
                        ImmutableArray<string>.Empty,
                        isMainToken: true);

                    public static VerifiedPrincipal CloneAsMain(VerifiedPrincipal principal) =>
                        principal with { IsMainToken = true };

                    public static void AssignMain(VerifiedPrincipal principal) =>
                        principal.IsMainToken = true;
                }
                """);

            var build = await BuildAsync(projectPath, root);

            Assert.NotEqual(0, build.ExitCode);
            Assert.Contains("CS1729", build.Output, StringComparison.Ordinal);
            Assert.Contains("CS8858", build.Output, StringComparison.Ordinal);
            Assert.Contains("CS0200", build.Output, StringComparison.Ordinal);
            Assert.Contains(nameof(VerifiedPrincipal), build.Output, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<(int ExitCode, string Output)> BuildAsync(
        string projectPath,
        string workingDirectory)
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
        startInfo.ArgumentList.Add("build");
        startInfo.ArgumentList.Add(projectPath);
        startInfo.ArgumentList.Add("--configuration");
        startInfo.ArgumentList.Add("Release");
        startInfo.ArgumentList.Add("/m:1");

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start dotnet build.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (
            process.ExitCode,
            $"{await output}{Environment.NewLine}{await error}");
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "MCServerLauncher.slnx")))
                return current.FullName;

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not find the MCServerLauncher repository root.");
    }
}
