using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Text;
using MCServerLauncher.Daemon.Plugin.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace MCServerLauncher.Daemon.Plugin.Generators.Tests;

public sealed class DaemonPluginSourceGeneratorTests
{
    private const string ManifestJson = """
        {
          "package": {
            "id": "community.example.health",
            "version": "1.0.0"
          },
          "entry": {
            "assembly": "Example.Plugin.dll",
            "type": "Example.Plugin.Generated.DaemonPluginAdapter"
          },
          "requires": {
            "api": "[2.0.0,3.0.0)",
            "features": ["event.publish", "instance.query", "rpc.register", "system.query"]
          }
        }
        """;

    private const string ModuleSource = """
        using System.Threading;
        using System.Threading.Tasks;
        using MCServerLauncher.Daemon.API.Errors;
        using MCServerLauncher.Daemon.API.Plugins;
        using MCServerLauncher.Daemon.Plugin.Sdk;
        using Microsoft.Extensions.DependencyInjection;
        using RustyOptions;

        namespace Example.Plugin;

        [DaemonPluginModule]
        public partial class HealthPlugin
        {
            public void ConfigureServices(IServiceCollection services, HealthPluginFeatures features)
            {
                _ = services;
                _ = features.Rpc;
                _ = features.Events;
                _ = features.InstanceCatalog;
                _ = features.InstanceQueries;
                _ = features.System;
            }

            public Task<Result<Unit, DaemonError>> StartAsync(CancellationToken cancellationToken)
                => Task.FromResult(PluginResult.Ok());

            public Task<Result<Unit, DaemonError>> StopAsync(CancellationToken cancellationToken)
                => Task.FromResult(PluginResult.Ok());
        }
        """;

    private const string MinimalModuleSource = """
        using System.Threading;
        using System.Threading.Tasks;
        using MCServerLauncher.Daemon.API.Errors;
        using MCServerLauncher.Daemon.API.Plugins;
        using MCServerLauncher.Daemon.Plugin.Sdk;
        using Microsoft.Extensions.DependencyInjection;
        using RustyOptions;

        namespace Example.Plugin;

        [DaemonPluginModule]
        public partial class MinimalPlugin
        {
            public void ConfigureServices(IServiceCollection services, MinimalPluginFeatures features)
            {
                _ = services;
                _ = features;
            }

            public Task<Result<Unit, DaemonError>> StartAsync(CancellationToken cancellationToken)
                => Task.FromResult(PluginResult.Ok());

            public Task<Result<Unit, DaemonError>> StopAsync(CancellationToken cancellationToken)
                => Task.FromResult(PluginResult.Ok());
        }
        """;

    [Fact]
    public void GeneratesAdapterMetadataAndFeatureSurface()
    {
        var (diagnostics, generated) = RunGenerator(ModuleSource, ManifestJson);

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("class DaemonPluginAdapter", generated, StringComparison.Ordinal);
        Assert.Contains("class HealthPluginFeatures", generated, StringComparison.Ordinal);
        Assert.Contains("class HealthPluginMetadata", generated, StringComparison.Ordinal);
        Assert.Contains("class HealthPluginServiceRegistration", generated, StringComparison.Ordinal);
        Assert.Contains("IPluginRpcRegistrar Rpc", generated, StringComparison.Ordinal);
        Assert.Contains("IPluginEventRegistrar Events", generated, StringComparison.Ordinal);
        Assert.Contains("IInstanceSnapshotSource InstanceCatalog", generated, StringComparison.Ordinal);
        Assert.Contains("IInstanceQueryApplication InstanceQueries", generated, StringComparison.Ordinal);
        Assert.Contains("ISystemQueryApplication System", generated, StringComparison.Ordinal);
        Assert.Contains("HealthPluginAuthorizedFeatures ForPrincipal", generated, StringComparison.Ordinal);
        Assert.Contains("ConfigureServices", generated, StringComparison.Ordinal);
        Assert.Contains("BuildServiceProvider", generated, StringComparison.Ordinal);
        Assert.Contains("AttachServices", generated, StringComparison.Ordinal);
        Assert.Contains("DetachServices", generated, StringComparison.Ordinal);
        Assert.Contains("IAsyncDisposable", generated, StringComparison.Ordinal);
        Assert.Contains("ManifestDigest", generated, StringComparison.Ordinal);
        Assert.Contains("community.example.health", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("IPluginContext Context { get; }", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("typeof(global::MCServerLauncher.Daemon.API.Plugins.IPluginContext)", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("typeof(global::MCServerLauncher.Daemon.API.State.IInstanceSnapshotSource)", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("typeof(global::MCServerLauncher.Daemon.API.Application.IInstanceApplication)", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("typeof(global::MCServerLauncher.Daemon.API.Application.IInstanceQueryApplication)", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("typeof(global::MCServerLauncher.Daemon.API.Application.IInstanceManagementApplication)", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("typeof(global::MCServerLauncher.Daemon.API.Plugins.ISystemQueryApplication)", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("typeof(global::MCServerLauncher.Daemon.API.Application.IOperationApplication)", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("typeof(global::MCServerLauncher.Daemon.API.Application.IOperationQueryApplication)", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("typeof(global::MCServerLauncher.Daemon.API.Application.IOperationControlApplication)", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("typeof(global::MCServerLauncher.Daemon.API.Application.IProvisioningApplication)", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsMissingManifestWhenModulePresent()
    {
        var (diagnostics, _) = RunGenerator(ModuleSource, manifestJson: null);
        Assert.Contains(diagnostics, d => d.Id == "MCSLPLG001");
    }

    [Fact]
    public void ReportsManualIDaemonPluginWhenManifestPresent()
    {
        const string manual = """
            using System.Threading;
            using System.Threading.Tasks;
            using MCServerLauncher.Daemon.API.Errors;
            using MCServerLauncher.Daemon.API.Plugins;
            using RustyOptions;

            namespace Example.Plugin;

            public sealed class ManualPlugin : IDaemonPlugin
            {
                public Result<Unit, DaemonError> Configure(IPluginContext context) => PluginResult.Ok();
                public Task<Result<Unit, DaemonError>> StartAsync(CancellationToken cancellationToken)
                    => Task.FromResult(PluginResult.Ok());
                public Task<Result<Unit, DaemonError>> StopAsync(CancellationToken cancellationToken)
                    => Task.FromResult(PluginResult.Ok());
            }
            """;

        var (diagnostics, _) = RunGenerator(manual, ManifestJson);
        Assert.Contains(diagnostics, d => d.Id == "MCSLPLG009");
    }

    [Fact]
    public void OperationQueryDoesNotExposeControlOrRawContext()
    {
        var manifest = ManifestJson.Replace(
            "[\"event.publish\", \"instance.query\", \"rpc.register\", \"system.query\"]",
            "[\"operation.query\"]",
            StringComparison.Ordinal);

        var (diagnostics, generated) = RunGenerator(MinimalModuleSource, manifest);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains("IOperationQueryApplication OperationQueries", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("IOperationControlApplication OperationControl", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("IOperationApplication", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("IPluginContext Context { get; }", generated, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "typeof(global::MCServerLauncher.Daemon.API.Plugins.IPluginContext)",
            generated,
            StringComparison.Ordinal);
    }

    [Fact]
    public void OperationCancelDoesNotExposeQuery()
    {
        var manifest = ManifestJson.Replace(
            "[\"event.publish\", \"instance.query\", \"rpc.register\", \"system.query\"]",
            "[\"operation.cancel\"]",
            StringComparison.Ordinal);

        var (diagnostics, generated) = RunGenerator(MinimalModuleSource, manifest);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains("IOperationControlApplication OperationControl", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("IOperationQueryApplication OperationQueries", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("IOperationApplication", generated, StringComparison.Ordinal);
    }

    private static (ImmutableArray<Diagnostic> Diagnostics, string Generated) RunGenerator(
        string source,
        string? manifestJson)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .ToList();

        // Ensure API + Sdk attribute assemblies are referenced.
        references.Add(MetadataReference.CreateFromFile(
            typeof(MCServerLauncher.Daemon.API.Plugins.IDaemonPlugin).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(
            typeof(MCServerLauncher.Daemon.Plugin.Sdk.DaemonPluginModuleAttribute).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(
            typeof(RustyOptions.Result<,>).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(
            typeof(Microsoft.Extensions.Logging.ILogger).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(
            Assembly.Load("Microsoft.Extensions.DependencyInjection.Abstractions").Location));
        references.Add(MetadataReference.CreateFromFile(
            Assembly.Load("Microsoft.Extensions.DependencyInjection").Location));
        references.Add(MetadataReference.CreateFromFile(
            typeof(System.Text.Json.JsonElement).Assembly.Location));

        var compilation = CSharpCompilation.Create(
            "GeneratorTests",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new DaemonPluginSourceGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);

        if (manifestJson is not null)
        {
            var additional = new InMemoryAdditionalText("mcsl-plugin.json", manifestJson);
            driver = driver.AddAdditionalTexts(ImmutableArray.Create<AdditionalText>(additional));
        }

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);
        var runResult = driver.GetRunResult();

        var generated = string.Join("\n", runResult.GeneratedTrees.Select(t => t.ToString()));
        var allDiagnostics = diagnostics
            .Concat(runResult.Diagnostics)
            .Concat(outputCompilation.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning))
            .ToImmutableArray();

        return (allDiagnostics, generated);
    }

    private sealed class InMemoryAdditionalText : AdditionalText
    {
        private readonly SourceText _text;

        public InMemoryAdditionalText(string path, string content)
        {
            Path = path;
            _text = SourceText.From(content, Encoding.UTF8);
        }

        public override string Path { get; }

        public override SourceText GetText(CancellationToken cancellationToken = default) => _text;
    }
}
