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
        var registrationTypeName = module.Name + "ServiceRegistration";
        var featuresFullName = moduleNs is null
            ? "global::" + featuresTypeName
            : "global::" + moduleNs + "." + featuresTypeName;
        var registrationFullName = moduleNs is null
            ? "global::" + registrationTypeName
            : "global::" + moduleNs + "." + registrationTypeName;
        var hasRpc = manifest.Features.Contains("rpc.register");
        var hasEvents = manifest.Features.Contains("event.publish");
        var hasInstanceQuery = manifest.Features.Contains("instance.query");
        var hasInstanceManage = manifest.Features.Contains("instance.manage");
        var hasOperationQuery = manifest.Features.Contains("operation.query");
        var hasOperationCancel = manifest.Features.Contains("operation.cancel");
        var hasProvisioning = manifest.Features.Contains("provisioning.manage");
        var hasStorage = manifest.Features.Contains("storage.private");
        var hasHttp = manifest.Features.Contains("network.http.listen");
        var hasAuth = manifest.Features.Contains("auth.verify");
        var hasSystem = manifest.Features.Contains("system.query");
        var hasAuthorizedApps = hasInstanceQuery || hasInstanceManage || hasOperationQuery ||
            hasOperationCancel || hasProvisioning || hasSystem;

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
                "        public global::MCServerLauncher.Daemon.API.State.IInstanceSnapshotSource InstanceCatalog { get; }");
            featureProperties.AppendLine(
                "        public global::MCServerLauncher.Daemon.API.Application.IInstanceQueryApplication InstanceQueries { get; }");
        }

        if (hasSystem)
        {
            featureProperties.AppendLine(
                "        public global::MCServerLauncher.Daemon.API.Plugins.ISystemQueryApplication System { get; }");
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

        if (hasInstanceManage)
        {
            featureProperties.AppendLine(
                "        public global::MCServerLauncher.Daemon.API.Application.IInstanceManagementApplication InstanceManagement { get; }");
        }

        if (hasOperationQuery)
        {
            featureProperties.AppendLine(
                "        public global::MCServerLauncher.Daemon.API.Application.IOperationQueryApplication OperationQueries { get; }");
        }

        if (hasOperationCancel)
        {
            featureProperties.AppendLine(
                "        public global::MCServerLauncher.Daemon.API.Application.IOperationControlApplication OperationControl { get; }");
        }

        if (hasProvisioning)
        {
            featureProperties.AppendLine(
                "        public global::MCServerLauncher.Daemon.API.Application.IProvisioningApplication Provisioning { get; }");
        }

        var featureBackingFields = new StringBuilder();
        var featureCtorAssignments = new StringBuilder();
        if (hasAuthorizedApps)
        {
            featureBackingFields.AppendLine(
                "        private readonly global::MCServerLauncher.Daemon.API.Application.ICallerContextFactory _callerContexts;");
            featureCtorAssignments.AppendLine("            _callerContexts = context.CallerContexts;");
            featureCtorAssignments.AppendLine(
                "            _hostCaller = context.CallerContexts.CreateHost(context.Identity, new string[] { " +
                string.Join(", ", manifest.Features.Select(static f => "\"" + f.Replace("\"", "\\\"") + "\"")) +
                " });");
        }

        if (hasRpc)
            featureCtorAssignments.AppendLine("            Rpc = context.Rpc;");
        if (hasEvents)
            featureCtorAssignments.AppendLine("            Events = context.Events;");
        if (hasInstanceQuery)
        {
            featureBackingFields.AppendLine(
                "        private readonly global::MCServerLauncher.Daemon.API.State.IInstanceSnapshotSource _instanceCatalog;");
            featureBackingFields.AppendLine(
                "        private readonly global::MCServerLauncher.Daemon.API.Application.IInstanceQueryApplication _instanceQueries;");
            featureCtorAssignments.AppendLine("            _instanceCatalog = context.InstanceCatalog;");
            featureCtorAssignments.AppendLine("            _instanceQueries = context.InstanceQueries;");
            featureCtorAssignments.AppendLine(
                "            InstanceCatalog = new global::MCServerLauncher.Daemon.API.Application.AuthorizedInstanceCatalog(_hostCaller!, _instanceCatalog);");
            featureCtorAssignments.AppendLine(
                "            InstanceQueries = new global::MCServerLauncher.Daemon.API.Application.AuthorizedInstanceQueryApplication(_hostCaller!, _instanceQueries);");
        }
        if (hasSystem)
        {
            featureBackingFields.AppendLine(
                "        private readonly global::MCServerLauncher.Daemon.API.Plugins.ISystemQueryApplication _system;");
            featureCtorAssignments.AppendLine("            _system = context.System;");
            featureCtorAssignments.AppendLine(
                "            System = new global::MCServerLauncher.Daemon.API.Application.AuthorizedSystemQueryApplication(_hostCaller!, _system);");
        }
        if (hasStorage)
            featureCtorAssignments.AppendLine("            Storage = context.Storage;");
        if (hasHttp)
            featureCtorAssignments.AppendLine("            HttpEndpoints = context.HttpEndpoints;");
        if (hasAuth)
            featureCtorAssignments.AppendLine("            Authentication = context.Authentication;");
        if (hasInstanceManage)
        {
            featureBackingFields.AppendLine(
                "        private readonly global::MCServerLauncher.Daemon.API.Application.IInstanceManagementApplication _instanceManagement;");
            featureCtorAssignments.AppendLine("            _instanceManagement = context.InstanceManagement;");
            featureCtorAssignments.AppendLine(
                "            InstanceManagement = new global::MCServerLauncher.Daemon.API.Application.AuthorizedInstanceManagementApplication(_hostCaller!, _instanceManagement);");
        }
        if (hasOperationQuery)
        {
            featureBackingFields.AppendLine(
                "        private readonly global::MCServerLauncher.Daemon.API.Application.IOperationQueryApplication _operationQueries;");
            featureCtorAssignments.AppendLine("            _operationQueries = context.OperationQueries;");
            featureCtorAssignments.AppendLine(
                "            OperationQueries = new global::MCServerLauncher.Daemon.API.Application.AuthorizedOperationQueryApplication(_hostCaller!, _operationQueries);");
        }
        if (hasOperationCancel)
        {
            featureBackingFields.AppendLine(
                "        private readonly global::MCServerLauncher.Daemon.API.Application.IOperationControlApplication _operationControl;");
            featureCtorAssignments.AppendLine("            _operationControl = context.OperationControl;");
            featureCtorAssignments.AppendLine(
                "            OperationControl = new global::MCServerLauncher.Daemon.API.Application.AuthorizedOperationControlApplication(_hostCaller!, _operationControl);");
        }
        if (hasProvisioning)
        {
            featureBackingFields.AppendLine(
                "        private readonly global::MCServerLauncher.Daemon.API.Application.IProvisioningApplication _provisioning;");
            featureCtorAssignments.AppendLine("            _provisioning = context.Provisioning;");
            featureCtorAssignments.AppendLine(
                "            Provisioning = new global::MCServerLauncher.Daemon.API.Application.AuthorizedProvisioningApplication(_hostCaller!, _provisioning);");
        }

        var registrationBody = new StringBuilder();
        registrationBody.AppendLine(
            "            services.Add(new global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor(typeof(" + featuresFullName + "), features));");
        registrationBody.AppendLine(
            "            services.Add(new global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor(typeof(global::MCServerLauncher.Daemon.API.Plugins.PluginIdentity), context.Identity));");
        registrationBody.AppendLine(
            "            services.Add(new global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor(typeof(global::Microsoft.Extensions.Logging.ILogger), context.Logger));");
        registrationBody.AppendLine(
            "            services.Add(new global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor(typeof(global::MCServerLauncher.Daemon.API.Plugins.IPluginErrorFactory), context.Errors));");
        registrationBody.AppendLine(
            "            services.Add(new global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor(typeof(global::MCServerLauncher.Daemon.API.Plugins.IPluginConfiguration), context.Configuration));");
        if (hasRpc)
        {
            registrationBody.AppendLine(
                "            services.Add(new global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor(typeof(global::MCServerLauncher.Daemon.API.Plugins.IPluginRpcRegistrar), context.Rpc));");
        }

        if (hasEvents)
        {
            registrationBody.AppendLine(
                "            services.Add(new global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor(typeof(global::MCServerLauncher.Daemon.API.Plugins.IPluginEventRegistrar), context.Events));");
        }

        if (hasStorage)
        {
            registrationBody.AppendLine(
                "            services.Add(new global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor(typeof(global::MCServerLauncher.Daemon.API.Plugins.IPluginPrivateStorage), context.Storage));");
        }

        if (hasHttp)
        {
            registrationBody.AppendLine(
                "            services.Add(new global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor(typeof(global::MCServerLauncher.Daemon.API.Plugins.IPluginHttpEndpointPolicy), context.HttpEndpoints));");
        }

        if (hasAuth)
        {
            registrationBody.AppendLine(
                "            services.Add(new global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor(typeof(global::MCServerLauncher.Daemon.API.Plugins.IPluginAuthentication), context.Authentication));");
        }

        var authorizedParameters = new List<string>
        {
            "global::MCServerLauncher.Daemon.API.Application.ICallerContext caller"
        };
        var authorizedArguments = new List<string> { "caller" };
        var authorizedAssignments = new StringBuilder();
        var authorizedProperties = new StringBuilder();

        if (hasInstanceQuery)
        {
            authorizedParameters.Add(
                "global::MCServerLauncher.Daemon.API.State.IInstanceSnapshotSource instanceCatalog");
            authorizedParameters.Add(
                "global::MCServerLauncher.Daemon.API.Application.IInstanceQueryApplication instanceQueries");
            authorizedArguments.Add("_instanceCatalog");
            authorizedArguments.Add("_instanceQueries");
            authorizedAssignments.AppendLine(
                "            InstanceCatalog = new global::MCServerLauncher.Daemon.API.Application.AuthorizedInstanceCatalog(caller, instanceCatalog);");
            authorizedAssignments.AppendLine(
                "            InstanceQueries = new global::MCServerLauncher.Daemon.API.Application.AuthorizedInstanceQueryApplication(caller, instanceQueries);");
            authorizedProperties.AppendLine(
                "        public global::MCServerLauncher.Daemon.API.State.IInstanceSnapshotSource InstanceCatalog { get; }");
            authorizedProperties.AppendLine(
                "        public global::MCServerLauncher.Daemon.API.Application.IInstanceQueryApplication InstanceQueries { get; }");
        }

        if (hasSystem)
        {
            authorizedParameters.Add(
                "global::MCServerLauncher.Daemon.API.Plugins.ISystemQueryApplication system");
            authorizedArguments.Add("_system");
            authorizedAssignments.AppendLine(
                "            System = new global::MCServerLauncher.Daemon.API.Application.AuthorizedSystemQueryApplication(caller, system);");
            authorizedProperties.AppendLine(
                "        public global::MCServerLauncher.Daemon.API.Plugins.ISystemQueryApplication System { get; }");
        }

        if (hasInstanceManage)
        {
            authorizedParameters.Add(
                "global::MCServerLauncher.Daemon.API.Application.IInstanceManagementApplication instanceManagement");
            authorizedArguments.Add("_instanceManagement");
            authorizedAssignments.AppendLine(
                "            InstanceManagement = new global::MCServerLauncher.Daemon.API.Application.AuthorizedInstanceManagementApplication(caller, instanceManagement);");
            authorizedProperties.AppendLine(
                "        public global::MCServerLauncher.Daemon.API.Application.IInstanceManagementApplication InstanceManagement { get; }");
        }

        if (hasOperationQuery)
        {
            authorizedParameters.Add(
                "global::MCServerLauncher.Daemon.API.Application.IOperationQueryApplication operationQueries");
            authorizedArguments.Add("_operationQueries");
            authorizedAssignments.AppendLine(
                "            OperationQueries = new global::MCServerLauncher.Daemon.API.Application.AuthorizedOperationQueryApplication(caller, operationQueries);");
            authorizedProperties.AppendLine(
                "        public global::MCServerLauncher.Daemon.API.Application.IOperationQueryApplication OperationQueries { get; }");
        }

        if (hasOperationCancel)
        {
            authorizedParameters.Add(
                "global::MCServerLauncher.Daemon.API.Application.IOperationControlApplication operationControl");
            authorizedArguments.Add("_operationControl");
            authorizedAssignments.AppendLine(
                "            OperationControl = new global::MCServerLauncher.Daemon.API.Application.AuthorizedOperationControlApplication(caller, operationControl);");
            authorizedProperties.AppendLine(
                "        public global::MCServerLauncher.Daemon.API.Application.IOperationControlApplication OperationControl { get; }");
        }

        if (hasProvisioning)
        {
            authorizedParameters.Add(
                "global::MCServerLauncher.Daemon.API.Application.IProvisioningApplication provisioning");
            authorizedArguments.Add("_provisioning");
            authorizedAssignments.AppendLine(
                "            Provisioning = new global::MCServerLauncher.Daemon.API.Application.AuthorizedProvisioningApplication(caller, provisioning);");
            authorizedProperties.AppendLine(
                "        public global::MCServerLauncher.Daemon.API.Application.IProvisioningApplication Provisioning { get; }");
        }

        var authorizedParameterLiteral = string.Join(",\n            ", authorizedParameters);
        var authorizedArgumentLiteral = string.Join(", ", authorizedArguments);

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
{{featureBackingFields}}{{(hasAuthorizedApps ? "        private readonly global::MCServerLauncher.Daemon.API.Application.ICallerContext _hostCaller;\n" : string.Empty)}}

        public {{featuresTypeName}}(global::MCServerLauncher.Daemon.API.Plugins.IPluginContext context)
        {
{{featureCtorAssignments}}        }

        private global::System.IServiceProvider? _services;

        /// <summary>
        /// Plugin-private service provider. Available after ConfigureServices and provider build complete.
        /// </summary>
        public global::System.IServiceProvider Services =>
            _services ?? throw new global::System.InvalidOperationException(
                "Plugin private services are available only after ConfigureServices completes.");

        public void AttachServices(global::System.IServiceProvider services) =>
            _services = services ?? throw new global::System.ArgumentNullException(nameof(services));

        public void DetachServices() => _services = null;
{{featureProperties}}{{(hasAuthorizedApps ? $@"
        /// <summary>
        /// Builds permission-checked application facades for a verified user principal.
        /// MCP tools must use this path; never silently fall back to Host.
        /// </summary>
        public {module.Name}AuthorizedFeatures ForPrincipal(global::MCServerLauncher.Daemon.API.Plugins.VerifiedPrincipal principal)
        {{
            var caller = _callerContexts.ForPrincipal(principal);
            return new {module.Name}AuthorizedFeatures({authorizedArgumentLiteral});
        }}
" : string.Empty)}}    }

{{(hasAuthorizedApps ? $@"
    /// <summary>
    /// Permission-checked application surfaces bound to a verified principal.
    /// </summary>
    public sealed class {module.Name}AuthorizedFeatures
    {{
        public {module.Name}AuthorizedFeatures(
            {authorizedParameterLiteral})
        {{
            Caller = caller ?? throw new global::System.ArgumentNullException(nameof(caller));
{authorizedAssignments}        }}

        public global::MCServerLauncher.Daemon.API.Application.ICallerContext Caller {{ get; }}
{authorizedProperties}    }}
" : string.Empty)}}

    /// <summary>
    /// Registers base services and declared feature facades into a plugin-private DI container.
    /// </summary>
    public static class {{registrationTypeName}}
    {
        public static void AddFeatureServices(
            global::Microsoft.Extensions.DependencyInjection.IServiceCollection services,
            global::MCServerLauncher.Daemon.API.Plugins.IPluginContext context,
            {{featuresFullName}} features)
        {
            if (services is null) throw new global::System.ArgumentNullException(nameof(services));
            if (context is null) throw new global::System.ArgumentNullException(nameof(context));
            if (features is null) throw new global::System.ArgumentNullException(nameof(features));
{{registrationBody}}        }
    }

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
    public sealed class DaemonPluginAdapter : global::MCServerLauncher.Daemon.API.Plugins.IDaemonPlugin, global::System.IAsyncDisposable, global::System.IDisposable
    {
        private readonly {{moduleFullName}} _module = new {{moduleFullName}}();
        private {{featuresFullName}}? _features;
        private global::Microsoft.Extensions.DependencyInjection.ServiceProvider? _services;
        private bool _disposed;

        public global::RustyOptions.Result<global::RustyOptions.Unit, global::MCServerLauncher.Daemon.API.Errors.DaemonError> Configure(
            global::MCServerLauncher.Daemon.API.Plugins.IPluginContext context)
        {
            if (_disposed)
                throw new global::System.ObjectDisposedException(nameof(DaemonPluginAdapter));

            _features = new {{featuresFullName}}(context);
            var services = new global::Microsoft.Extensions.DependencyInjection.ServiceCollection();
            {{registrationFullName}}.AddFeatureServices(services, context, _features);
            try
            {
                _module.ConfigureServices(services, _features);
            }
            catch (global::System.Exception exception)
            {
                return global::RustyOptions.Result.Err<global::RustyOptions.Unit, global::MCServerLauncher.Daemon.API.Errors.DaemonError>(
                    context.Errors.Create(
                        "plugin_configure_services_failed",
                        "Plugin ConfigureServices threw: " + exception.Message));
            }

            try
            {
                _services = global::Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(
                    services,
                    new global::Microsoft.Extensions.DependencyInjection.ServiceProviderOptions
                    {
                        ValidateScopes = true,
                        ValidateOnBuild = true,
                    });
                _features.AttachServices(_services);
            }
            catch (global::System.Exception exception)
            {
                return global::RustyOptions.Result.Err<global::RustyOptions.Unit, global::MCServerLauncher.Daemon.API.Errors.DaemonError>(
                    context.Errors.Create(
                        "plugin_service_provider_failed",
                        "Plugin private service provider failed to build: " + exception.Message));
            }

            return global::RustyOptions.Result.Ok<global::RustyOptions.Unit, global::MCServerLauncher.Daemon.API.Errors.DaemonError>(
                global::RustyOptions.Unit.Default);
        }

        public global::System.Threading.Tasks.Task<global::RustyOptions.Result<global::RustyOptions.Unit, global::MCServerLauncher.Daemon.API.Errors.DaemonError>> StartAsync(
            global::System.Threading.CancellationToken cancellationToken)
            => _module.StartAsync(cancellationToken);

        public global::System.Threading.Tasks.Task<global::RustyOptions.Result<global::RustyOptions.Unit, global::MCServerLauncher.Daemon.API.Errors.DaemonError>> StopAsync(
            global::System.Threading.CancellationToken cancellationToken)
            => _module.StopAsync(cancellationToken);

        public void Dispose()
        {
            DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        public async global::System.Threading.Tasks.ValueTask DisposeAsync()
        {
            if (_disposed)
                return;
            _disposed = true;

            try
            {
                _features?.DetachServices();
                if (_services is not null)
                    await _services.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                _services = null;
                _features = null;
            }
        }
    }
}
""";
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
