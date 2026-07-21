using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using MCServerLauncher.Daemon.Plugin.Generators.Diagnostics;
using MCServerLauncher.Daemon.Plugin.Generators.Manifest;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace MCServerLauncher.Daemon.Plugin.Generators;

[Generator]
public sealed class DaemonPluginSourceGenerator : IIncrementalGenerator
{
    private const string ModuleAttributeMetadataName = "MCServerLauncher.Daemon.Plugin.Sdk.DaemonPluginModuleAttribute";
    private const string IDaemonPluginMetadataName = "MCServerLauncher.Daemon.API.Plugins.IDaemonPlugin";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var manifests = context.AdditionalTextsProvider
            .Where(static text => text.Path.EndsWith("mcsl-plugin.json", System.StringComparison.OrdinalIgnoreCase))
            .Select(static (text, cancellationToken) =>
            {
                var content = text.GetText(cancellationToken)?.ToString() ?? string.Empty;
                return (Path: text.Path, Content: content, Parsed: PluginManifestParser.Parse(content, text.Path));
            })
            .Collect();

        var modules = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                ModuleAttributeMetadataName,
                static (node, _) => node is ClassDeclarationSyntax,
                static (ctx, _) => (INamedTypeSymbol)ctx.TargetSymbol)
            .Collect();

        var manualPlugins = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax { BaseList: not null },
                static (ctx, _) =>
                {
                    var symbol = ctx.SemanticModel.GetDeclaredSymbol(ctx.Node) as INamedTypeSymbol;
                    return symbol;
                })
            .Where(static symbol => symbol is not null)
            .Select(static (symbol, _) => symbol!)
            .Where(static symbol => ImplementsInterface(symbol, IDaemonPluginMetadataName))
            .Collect();

        var combined = manifests.Combine(modules).Combine(manualPlugins);

        context.RegisterSourceOutput(combined, static (spc, source) =>
        {
            var ((manifests, modules), manualPlugins) = source;
            Execute(spc, manifests, modules, manualPlugins);
        });
    }

    private static void Execute(
        SourceProductionContext context,
        ImmutableArray<(string Path, string Content, ParsedPluginManifest Parsed)> manifests,
        ImmutableArray<INamedTypeSymbol> modules,
        ImmutableArray<INamedTypeSymbol> manualPlugins)
    {
        // When neither a module nor a manifest is present (e.g. building the SDK package
        // itself), stay silent. Diagnostics only fire for plugin projects that opt in.
        var hasModule = !modules.IsDefaultOrEmpty;
        var hasManifest = !manifests.IsDefaultOrEmpty;

        // Manual IDaemonPlugin implementations are forbidden only when the project is
        // using the SDK module model (has a [DaemonPluginModule] or a manifest).
        if (hasModule || hasManifest)
        {
            foreach (var manual in manualPlugins.Distinct(SymbolEqualityComparer.Default).OfType<INamedTypeSymbol>())
            {
                // Generated adapter intentionally implements IDaemonPlugin.
                if (manual.Name == "DaemonPluginAdapter" &&
                    manual.ContainingNamespace?.ToDisplayString().EndsWith(".Generated", StringComparison.Ordinal) == true)
                {
                    continue;
                }

                // [DaemonPluginModule] classes are partial modules, not adapters.
                if (HasModuleAttribute(manual))
                    continue;

                context.ReportDiagnostic(Diagnostic.Create(
                    PluginSdkDiagnostics.ManualIDaemonPlugin,
                    manual.Locations.FirstOrDefault() ?? Location.None,
                    manual.ToDisplayString()));
            }
        }

        if (!hasModule && !hasManifest)
            return;

        if (!hasManifest)
        {
            context.ReportDiagnostic(Diagnostic.Create(PluginSdkDiagnostics.MissingManifest, Location.None));
            return;
        }

        if (manifests.Length > 1)
        {
            context.ReportDiagnostic(Diagnostic.Create(PluginSdkDiagnostics.MultipleManifests, Location.None));
            return;
        }

        var manifestEntry = manifests[0];
        var parsed = manifestEntry.Parsed;
        if (!parsed.IsValid)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                PluginSdkDiagnostics.MalformedManifest,
                Location.None,
                parsed.Error));
            return;
        }

        // Sorted-set diagnostic (non-blocking warning).
        var sorted = parsed.Features.OrderBy(static f => f, StringComparer.Ordinal).ToArray();
        if (!parsed.Features.SequenceEqual(sorted, StringComparer.Ordinal))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                PluginSdkDiagnostics.UnsortedFeatures,
                Location.None,
                string.Join(", ", sorted)));
        }

        if (!hasModule)
        {
            context.ReportDiagnostic(Diagnostic.Create(PluginSdkDiagnostics.MissingModule, Location.None));
            return;
        }

        if (modules.Length > 1)
        {
            context.ReportDiagnostic(Diagnostic.Create(PluginSdkDiagnostics.MultipleModules, Location.None));
            return;
        }

        var module = modules[0];
        if (!module.DeclaringSyntaxReferences.Any(static r =>
                r.GetSyntax() is ClassDeclarationSyntax cds && cds.Modifiers.Any(SyntaxKind.PartialKeyword)))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                PluginSdkDiagnostics.ModuleNotPartial,
                module.Locations.FirstOrDefault() ?? Location.None,
                module.Name));
            return;
        }

        var source = Generate(module, parsed);
        context.AddSource($"{module.Name}.PluginSdk.g.cs", SourceText.From(source, Encoding.UTF8));
    }

    private static bool HasModuleAttribute(INamedTypeSymbol symbol)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() == ModuleAttributeMetadataName)
                return true;
        }

        return false;
    }

    private static bool ImplementsInterface(INamedTypeSymbol symbol, string metadataName)
    {
        foreach (var iface in symbol.AllInterfaces)
        {
            if (iface.ToDisplayString() == metadataName)
                return true;
        }

        return false;
    }

    private static string Generate(INamedTypeSymbol module, ParsedPluginManifest manifest)
    {
        var moduleNs = module.ContainingNamespace.IsGlobalNamespace
            ? null
            : module.ContainingNamespace.ToDisplayString();
        var adapterNs = moduleNs is null ? "Generated" : moduleNs + ".Generated";
        var moduleFullName = module.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var featuresTypeName = module.Name + "Features";
        var metadataTypeName = module.Name + "Metadata";
        var featuresFullName = moduleNs is null
            ? "global::" + featuresTypeName
            : "global::" + moduleNs + "." + featuresTypeName;
        var hasRpc = manifest.Features.Contains("rpc.register");
        var hasEvents = manifest.Features.Contains("event.publish");
        var hasInstanceQuery = manifest.Features.Contains("instance.query");
        var hasStorage = manifest.Features.Contains("storage.private");
        var hasHttp = manifest.Features.Contains("network.http.listen");
        var hasAuth = manifest.Features.Contains("auth.verify");
        var hasSystem = manifest.Features.Contains("system.query");

        var featureProperties = new StringBuilder();
        if (hasRpc)
        {
            featureProperties.AppendLine(
                "        public global::MCServerLauncher.Daemon.API.Plugins.IPluginRpcRegistrar Rpc { get; }");
        }

        if (hasEvents)
        {
            featureProperties.AppendLine(
                "        public global::MCServerLauncher.Daemon.API.Plugins.IPluginEventRegistrar Events { get; }");
        }

        if (hasInstanceQuery)
        {
            featureProperties.AppendLine(
                "        public global::MCServerLauncher.Daemon.API.State.IInstanceSnapshotSource Instances { get; }");
        }
        if (hasStorage)
        {
            featureProperties.AppendLine(
                "        public global::MCServerLauncher.Daemon.API.Plugins.IPluginPrivateStorage Storage { get; }");
        }

        if (hasHttp)
        {
            featureProperties.AppendLine(
                "        public global::MCServerLauncher.Daemon.API.Plugins.IPluginHttpEndpointPolicy HttpEndpoints { get; }");
        }

        if (hasAuth)
        {
            featureProperties.AppendLine(
                "        public global::MCServerLauncher.Daemon.API.Plugins.IPluginAuthentication Authentication { get; }");
        }


        var featureCtorAssignments = new StringBuilder();
        featureCtorAssignments.AppendLine("            Context = context;");
        if (hasRpc)
            featureCtorAssignments.AppendLine("            Rpc = context.Rpc;");
        if (hasEvents)
            featureCtorAssignments.AppendLine("            Events = context.Events;");
        if (hasInstanceQuery)
            featureCtorAssignments.AppendLine("            Instances = context.Instances;");
        if (hasStorage)
            featureCtorAssignments.AppendLine("            Storage = context.Storage;");
        if (hasHttp)
            featureCtorAssignments.AppendLine("            HttpEndpoints = context.HttpEndpoints;");
        if (hasAuth)
            featureCtorAssignments.AppendLine("            Authentication = context.Authentication;");

        var featuresLiteral = string.Join(", ",
            manifest.Features.Select(static f => "\"" + f.Replace("\"", "\\\"") + "\""));

        var moduleNamespaceOpen = moduleNs is null ? string.Empty : "namespace " + moduleNs + "\n{\n";
        var moduleNamespaceClose = moduleNs is null ? string.Empty : "\n}\n";

        return $$"""
// <auto-generated />
#nullable enable
#pragma warning disable CS1591

{{moduleNamespaceOpen}}    /// <summary>
    /// Feature surfaces declared by mcsl-plugin.json for {{module.Name}}.
    /// Only features listed in the manifest are present.
    /// </summary>
    public sealed class {{featuresTypeName}}
    {
        public {{featuresTypeName}}(global::MCServerLauncher.Daemon.API.Plugins.IPluginContext context)
        {
{{featureCtorAssignments}}        }

        public global::MCServerLauncher.Daemon.API.Plugins.IPluginContext Context { get; }
{{featureProperties}}    }

    /// <summary>
    /// Normalized identity/metadata embedded from mcsl-plugin.json.
    /// Runtime admission compares the digest with the on-disk manifest.
    /// </summary>
    public static class {{metadataTypeName}}
    {
        public const string PackageId = "{{Escape(manifest.PackageId)}}";
        public const string PackageVersion = "{{Escape(manifest.PackageVersion)}}";
        public const string EntryAssembly = "{{Escape(manifest.EntryAssembly)}}";
        public const string EntryType = "{{Escape(manifest.EntryType)}}";
        public const string ApiRange = "{{Escape(manifest.ApiRange)}}";
        public const string ManifestDigest = "{{Escape(manifest.Digest)}}";
        public static readonly string[] Features = new string[] { {{featuresLiteral}} };
    }
{{moduleNamespaceClose}}
namespace {{adapterNs}}
{
    /// <summary>
    /// Generated <see cref="global::MCServerLauncher.Daemon.API.Plugins.IDaemonPlugin"/> adapter.
    /// Point mcsl-plugin.json entry.type at this type.
    /// </summary>
    public sealed class DaemonPluginAdapter : global::MCServerLauncher.Daemon.API.Plugins.IDaemonPlugin
    {
        private readonly {{moduleFullName}} _module = new {{moduleFullName}}();
        private {{featuresFullName}}? _features;

        public global::RustyOptions.Result<global::RustyOptions.Unit, global::MCServerLauncher.Daemon.API.Errors.DaemonError> Configure(
            global::MCServerLauncher.Daemon.API.Plugins.IPluginContext context)
        {
            _features = new {{featuresFullName}}(context);
            return _module.Configure(context, _features);
        }

        public global::System.Threading.Tasks.Task<global::RustyOptions.Result<global::RustyOptions.Unit, global::MCServerLauncher.Daemon.API.Errors.DaemonError>> StartAsync(
            global::System.Threading.CancellationToken cancellationToken)
            => _module.StartAsync(cancellationToken);

        public global::System.Threading.Tasks.Task<global::RustyOptions.Result<global::RustyOptions.Unit, global::MCServerLauncher.Daemon.API.Errors.DaemonError>> StopAsync(
            global::System.Threading.CancellationToken cancellationToken)
            => _module.StopAsync(cancellationToken);
    }
}
""";
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
