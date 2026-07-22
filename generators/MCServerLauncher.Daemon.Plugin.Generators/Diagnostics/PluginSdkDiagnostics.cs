using Microsoft.CodeAnalysis;

namespace MCServerLauncher.Daemon.Plugin.Generators.Diagnostics;

internal static class PluginSdkDiagnostics
{
    private const string Category = "MCServerLauncher.Plugin.Sdk";

    public static readonly DiagnosticDescriptor MissingManifest = new(
        id: "MCSLPLG001",
        title: "Missing mcsl-plugin.json",
        messageFormat: "No mcsl-plugin.json AdditionalFile was found for this project",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MultipleManifests = new(
        id: "MCSLPLG002",
        title: "Multiple mcsl-plugin.json files",
        messageFormat: "Multiple mcsl-plugin.json AdditionalFiles were found; only one is allowed",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MalformedManifest = new(
        id: "MCSLPLG003",
        title: "Malformed mcsl-plugin.json",
        messageFormat: "mcsl-plugin.json is invalid: {0}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnknownFeature = new(
        id: "MCSLPLG004",
        title: "Unknown plugin feature",
        messageFormat: "Plugin feature '{0}' is not in the FeatureCatalog vocabulary",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DuplicateFeature = new(
        id: "MCSLPLG005",
        title: "Duplicate plugin feature",
        messageFormat: "Plugin feature '{0}' is declared more than once",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsortedFeatures = new(
        id: "MCSLPLG006",
        title: "Unsorted plugin features",
        messageFormat: "requires.features must be sorted lexicographically; expected [{0}]",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MissingModule = new(
        id: "MCSLPLG007",
        title: "Missing [DaemonPluginModule]",
        messageFormat: "No class annotated with [DaemonPluginModule] was found",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MultipleModules = new(
        id: "MCSLPLG008",
        title: "Multiple [DaemonPluginModule] classes",
        messageFormat: "Only one [DaemonPluginModule] class is allowed per compilation",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ManualIDaemonPlugin = new(
        id: "MCSLPLG009",
        title: "Manual IDaemonPlugin implementation",
        messageFormat: "Class '{0}' implements IDaemonPlugin manually; use [DaemonPluginModule] and the generated adapter instead",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ModuleNotPartial = new(
        id: "MCSLPLG010",
        title: "Plugin module must be partial",
        messageFormat: "Class '{0}' annotated with [DaemonPluginModule] must be declared partial",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ConflictingFeature = new(
        id: "MCSLPLG011",
        title: "Conflicting plugin features",
        messageFormat: "Plugin features '{0}' and '{1}' cannot be declared together",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor EntryMismatch = new(
        id: "MCSLPLG012",
        title: "Plugin entry mismatch",
        messageFormat: "Manifest entry '{0}' does not match generated entry '{1}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedApiRange = new(
        id: "MCSLPLG013",
        title: "Unsupported plugin API range",
        messageFormat: "Manifest API range '{0}' does not include Plugin API 2.0.0",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor RawHostContextUse = new(
        id: "MCSLPLG014",
        title: "Raw plugin context use",
        messageFormat: "Plugin module '{0}' references raw IPluginContext; use the generated feature surface",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MissingExplicitJsonMetadata = new(
        id: "MCSLPLG015",
        title: "Missing explicit JSON metadata",
        messageFormat: "Call to '{0}' passes null or default for required JsonTypeInfo parameter '{1}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UndeclaredFeatureSurface = new(
        id: "MCSLPLG016",
        title: "Undeclared plugin feature surface",
        messageFormat: "Feature surface '{0}' requires manifest feature '{1}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
