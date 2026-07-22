using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using MCServerLauncher.Daemon.Plugin.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace MCServerLauncher.Daemon.Plugin.Generators.Tests;

public sealed class DaemonPluginSourceGeneratorTests
{
    public static TheoryData<string, string> Preview1FeatureSurfaces => new()
    {
        { "system.query", "System" },
        { "instance.query", "InstanceCatalog" },
        { "instance.manage", "InstanceManagement" },
        { "operation.query", "OperationQueries" },
        { "operation.cancel", "OperationControl" },
        { "provisioning.manage", "Provisioning" },
        { "network.http.listen", "HttpEndpoints" },
        { "auth.verify", "Authentication" },
        { "storage.private", "Storage" },
    };

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
        Assert.Contains("IGeneratedDaemonPluginAdapter", generated, StringComparison.Ordinal);
        Assert.Contains("PluginAdapterMetadata Metadata", generated, StringComparison.Ordinal);
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
        Assert.Contains("ImmutableArray<string> Features", generated, StringComparison.Ordinal);
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
    public void ReportsMultipleManifests()
    {
        var (diagnostics, _) = RunGenerator(
            ModuleSource,
            ManifestJson,
            ("second/mcsl-plugin.json", ManifestJson));

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "MCSLPLG002");
    }

    [Fact]
    public void ReportsMissingModuleWhenManifestPresent()
    {
        const string source = "namespace Example.Plugin; public sealed class NotAPluginModule { }";

        var (diagnostics, _) = RunGenerator(source, ManifestJson);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "MCSLPLG007");
    }

    [Fact]
    public void ReportsMultiplePluginModules()
    {
        var source = MinimalModuleSource +
                     "\nnamespace Example.Plugin { [DaemonPluginModule] public partial class SecondPlugin { } }";

        var (diagnostics, _) = RunGenerator(source, CreatePreviewManifest());

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "MCSLPLG008");
    }

    [Fact]
    public void ReportsNonPartialPluginModule()
    {
        var source = MinimalModuleSource.Replace(
            "public partial class MinimalPlugin",
            "public class MinimalPlugin",
            StringComparison.Ordinal);

        var (diagnostics, _) = RunGenerator(source, CreatePreviewManifest());

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "MCSLPLG010");
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

    [Fact]
    public void NormalizesVersionsFeatureOrderAndDigestDeterministically()
    {
        var normalizedInput = ManifestJson
            .Replace("\"version\": \"1.0.0\"", "\"version\": \"01.00\"", StringComparison.Ordinal)
            .Replace("[2.0.0,3.0.0)", "[2.0, 3.0)", StringComparison.Ordinal);
        var reorderedInput = normalizedInput.Replace(
            "[\"event.publish\", \"instance.query\", \"rpc.register\", \"system.query\"]",
            "[\"system.query\", \"rpc.register\", \"instance.query\", \"event.publish\"]",
            StringComparison.Ordinal);
        var schemaInput = normalizedInput.Replace(
            "{\n  \"package\"",
            "{\n  \"$schema\": \"https://mcsl-team.github.io/schemas/mcsl-plugin-2.0.schema.json\",\n  \"package\"",
            StringComparison.Ordinal);

        var (normalizedDiagnostics, normalizedGenerated) = RunGenerator(ModuleSource, normalizedInput);
        var (reorderedDiagnostics, reorderedGenerated) = RunGenerator(ModuleSource, reorderedInput);
        var (schemaDiagnostics, schemaGenerated) = RunGenerator(ModuleSource, schemaInput);

        Assert.DoesNotContain(normalizedDiagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains(reorderedDiagnostics, diagnostic => diagnostic.Id == "MCSLPLG006");
        Assert.DoesNotContain(schemaDiagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains("PackageVersion = \"1.0.0\"", normalizedGenerated, StringComparison.Ordinal);
        Assert.Contains("ApiRange = \"[2.0.0, 3.0.0)\"", normalizedGenerated, StringComparison.Ordinal);
        Assert.Equal(ExtractManifestDigest(normalizedGenerated), ExtractManifestDigest(reorderedGenerated));
        Assert.Equal(ExtractManifestDigest(normalizedGenerated), ExtractManifestDigest(schemaGenerated));
    }

    [Fact]
    public void ParsesEscapedCanonicalValuesWithoutStringBoundaryConfusion()
    {
        var manifest = ManifestJson
            .Replace("community.example.health", "community.example.\\u0068ealth", StringComparison.Ordinal)
            .Replace("Example.Plugin.dll", "Example.Plugin\\u002Edll", StringComparison.Ordinal)
            .Replace("Example.Plugin.Generated.DaemonPluginAdapter", "Example.Plugin.Generated.\\u0044aemonPluginAdapter", StringComparison.Ordinal);

        var (diagnostics, generated) = RunGenerator(ModuleSource, manifest);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains("community.example.health", generated, StringComparison.Ordinal);
        Assert.Contains("Example.Plugin.dll", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void BracesAndEscapedQuotesInsideAStringDoNotHideUnknownFields()
    {
        var manifest = ManifestJson.Replace(
            "\"package\": {",
            "\"note\": \"literal } { \\\"quoted\\\" text\",\n  \"package\": {",
            StringComparison.Ordinal);

        var (diagnostics, _) = RunGenerator(ModuleSource, manifest);

        Assert.Contains(diagnostics, item => item.Id == "MCSLPLG003");
        var diagnostic = diagnostics.First(item => item.Id == "MCSLPLG003");
        Assert.Contains("Unknown manifest field 'note'", diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\"version\": \"1.0.0\"", "\"version\": \"1.0.0\", \"version\": \"1.0.1\"")]
    [InlineData("\"requires\": {", "\"requires\": { \"api\": \"[2.0.0,3.0.0)\", \"api\": \"[3.0.0,4.0.0)\", \"features\": [], \"nested\": {")]
    public void RejectsDuplicateJsonProperties(string original, string replacement)
    {
        var manifest = ManifestJson.Replace(original, replacement, StringComparison.Ordinal);

        var (diagnostics, _) = RunGenerator(ModuleSource, manifest);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "MCSLPLG003");
    }

    [Theory]
    [InlineData("}")]
    [InlineData("]")]
    public void RejectsMalformedEndOfFile(string closingToken)
    {
        var last = ManifestJson.LastIndexOf(closingToken, StringComparison.Ordinal);
        Assert.True(last >= 0);
        var manifest = ManifestJson.Remove(last, 1);

        var (diagnostics, _) = RunGenerator(ModuleSource, manifest);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "MCSLPLG003");
    }

    [Fact]
    public void ReportsUnknownDuplicateAndUnsortedFeaturesSeparately()
    {
        var manifest = WithFeatureArray(
            "[\"system.query\", \"unknown.feature\", \"system.query\", \"instance.query\"]");

        var (diagnostics, _) = RunGenerator(ModuleSource, manifest);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "MCSLPLG004");
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "MCSLPLG005");
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "MCSLPLG006");
    }

    [Fact]
    public void ReportsUnsupportedApiRange()
    {
        var manifest = ManifestJson.Replace("[2.0.0,3.0.0)", "[3.0.0,4.0.0)", StringComparison.Ordinal);

        var (diagnostics, _) = RunGenerator(ModuleSource, manifest);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "MCSLPLG013");
    }

    [Fact]
    public void ReportsGeneratedEntryMismatch()
    {
        var manifest = ManifestJson.Replace(
            "Example.Plugin.Generated.DaemonPluginAdapter",
            "Example.Plugin.WrongAdapter",
            StringComparison.Ordinal);

        var (diagnostics, _) = RunGenerator(ModuleSource, manifest);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "MCSLPLG012");
    }

    [Fact]
    public void ReportsRawPluginContextInModuleShape()
    {
        var source = MinimalModuleSource.Replace(
            "public void ConfigureServices",
            "private IPluginContext? _rawContext;\n\n            public void ConfigureServices",
            StringComparison.Ordinal);
        var manifest = WithFeatureArray("[]");

        var (diagnostics, _) = RunGenerator(source, manifest);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "MCSLPLG014");
    }

    [Fact]
    public void ReportsRawPluginContextUseInsideModuleBody()
    {
        var source = MinimalModuleSource.Replace(
            "_ = services;",
            "_ = services;\n                _ = typeof(IPluginContext);",
            StringComparison.Ordinal);
        var manifest = WithFeatureArray("[]");

        var (diagnostics, _) = RunGenerator(source, manifest);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "MCSLPLG014");
    }

    [Theory]
    [MemberData(nameof(Preview1FeatureSurfaces))]
    public void EveryPreview1FeatureGeneratesItsDeclaredSurface(string feature, string property)
    {
        var source = CreatePreviewModuleSource(property);
        var manifest = CreatePreviewManifest(feature);

        var (diagnostics, generated) = RunGenerator(source, manifest);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains($" {property} {{ get; }}", generated, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Preview1FeatureSurfaces))]
    public void EveryPreview1SurfaceIsUnavailableWhenUndeclared(string feature, string property)
    {
        var source = CreatePreviewModuleSource(property);
        var manifest = CreatePreviewManifest();

        var (diagnostics, _) = RunGenerator(source, manifest);

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Id == "MCSLPLG016" &&
                          diagnostic.GetMessage().Contains(property, StringComparison.Ordinal) &&
                          diagnostic.GetMessage().Contains(feature, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("null!")]
    [InlineData("default!")]
    [InlineData("default(System.Text.Json.Serialization.Metadata.JsonTypeInfo<string>)!")]
    public void ReportsNullOrDefaultExplicitJsonMetadata(string metadataExpression)
    {
        var source = CreateJsonMetadataModuleSource(metadataExpression);

        var (diagnostics, _) = RunGenerator(source, CreatePreviewManifest());

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "MCSLPLG015");
    }

    [Fact]
    public void AcceptsNonNullExplicitJsonMetadataExpression()
    {
        var source = CreateJsonMetadataModuleSource("typeInfo", includeTypeInfoParameter: true);

        var (diagnostics, _) = RunGenerator(source, CreatePreviewManifest());

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "MCSLPLG015");
    }

    private static string WithFeatureArray(string featureArray) => ManifestJson.Replace(
        "[\"event.publish\", \"instance.query\", \"rpc.register\", \"system.query\"]",
        featureArray,
        StringComparison.Ordinal);

    private static string CreatePreviewManifest(params string[] features)
    {
        var featureArray = "[" + string.Join(", ", features.Select(feature => $"\"{feature}\"")) + "]";
        return WithFeatureArray(featureArray).Replace(
            "Example.Plugin.Generated.DaemonPluginAdapter",
            "Example.Plugin.Generated.DaemonPluginAdapter",
            StringComparison.Ordinal);
    }

    private static string CreatePreviewModuleSource(string property) => $$"""
        using System.Threading;
        using System.Threading.Tasks;
        using MCServerLauncher.Daemon.API.Errors;
        using MCServerLauncher.Daemon.API.Plugins;
        using MCServerLauncher.Daemon.Plugin.Sdk;
        using Microsoft.Extensions.DependencyInjection;
        using RustyOptions;

        namespace Example.Plugin;

        [DaemonPluginModule]
        public partial class PreviewPlugin
        {
            public void ConfigureServices(IServiceCollection services, PreviewPluginFeatures features)
            {
                _ = services;
                _ = features.{{property}};
            }

            public Task<Result<Unit, DaemonError>> StartAsync(CancellationToken cancellationToken)
                => Task.FromResult(PluginResult.Ok());

            public Task<Result<Unit, DaemonError>> StopAsync(CancellationToken cancellationToken)
                => Task.FromResult(PluginResult.Ok());
        }
        """;

    private static string CreateJsonMetadataModuleSource(
        string metadataExpression,
        bool includeTypeInfoParameter = false)
    {
        var parameter = includeTypeInfoParameter
            ? ", System.Text.Json.Serialization.Metadata.JsonTypeInfo<string> typeInfo"
            : string.Empty;
        return MinimalModuleSource.Replace(
            "public void ConfigureServices(IServiceCollection services, MinimalPluginFeatures features)",
            "public void ConfigureServices(IServiceCollection services, MinimalPluginFeatures features" + parameter + ")",
            StringComparison.Ordinal).Replace(
            "_ = services;",
            "_ = services;\n                IPluginConfiguration configuration = null!;\n" +
            "                _ = configuration.Get<string>(" + metadataExpression + ");",
            StringComparison.Ordinal);
    }

    private static string ExtractManifestDigest(string generated)
    {
        var match = Regex.Match(
            generated,
            "ManifestDigest = \\\"(?<digest>[0-9a-f]{64})\\\"",
            RegexOptions.CultureInvariant);
        Assert.True(match.Success, "Generated metadata did not contain a normalized manifest digest.");
        return match.Groups["digest"].Value;
    }

    private static (ImmutableArray<Diagnostic> Diagnostics, string Generated) RunGenerator(
        string source,
        string? manifestJson,
        params (string Path, string Content)[] additionalManifests)
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

        var additionalTexts = ImmutableArray.CreateBuilder<AdditionalText>();
        if (manifestJson is not null)
            additionalTexts.Add(new InMemoryAdditionalText("mcsl-plugin.json", manifestJson));
        foreach (var (path, content) in additionalManifests)
            additionalTexts.Add(new InMemoryAdditionalText(path, content));
        if (additionalTexts.Count > 0)
            driver = driver.AddAdditionalTexts(additionalTexts.ToImmutable());

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
