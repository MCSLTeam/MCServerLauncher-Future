using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace MCServerLauncher.Daemon.API.Plugins;

public enum PluginExtensionPointKind
{
    Provider = 0,
    Registry = 1,
    Pipeline = 2,
    Hook = 3,
    Override = 4,
    Ui = 5,
    Event = 6,
    Command = 7
}

public sealed record PluginExtensionPointDescriptor(
    string Id,
    PluginExtensionPointKind Kind,
    string Summary,
    bool AllowsMultiple,
    bool AllowsOverride);

public sealed record PluginExtensionPointDeclaration(
    string Id,
    PluginExtensionPointKind Kind,
    int Priority = 0,
    string? Replaces = null,
    ImmutableArray<string> Conflicts = default,
    string? DefaultFor = null);

public sealed record PluginExtensionPointAdmissionResult(
    bool IsAdmitted,
    string Code,
    ImmutableArray<string> Diagnostics);

public static class ExtensionPointCatalog
{
    private static readonly ImmutableArray<PluginExtensionPointDescriptor> AllDescriptors = CreateAll();
    private static readonly ImmutableDictionary<string, PluginExtensionPointDescriptor> Lookup = AllDescriptors.ToImmutableDictionary(descriptor => descriptor.Id, StringComparer.Ordinal);

    public static ImmutableArray<PluginExtensionPointDescriptor> All => AllDescriptors;

    public static bool TryGet(string id, [NotNullWhen(true)] out PluginExtensionPointDescriptor? descriptor) =>
        Lookup.TryGetValue(id, out descriptor);

    public static bool IsKnown(string id) => Lookup.ContainsKey(id);

    public static PluginExtensionPointAdmissionResult ValidateDeclarations(IEnumerable<PluginExtensionPointDeclaration> declarations)
    {
        ArgumentNullException.ThrowIfNull(declarations);

        List<string> diagnostics = [];
        Dictionary<string, PluginExtensionPointDeclaration> seen = new(StringComparer.Ordinal);
        foreach (var declaration in declarations)
        {
            if (string.IsNullOrWhiteSpace(declaration.Id))
            {
                diagnostics.Add("extension_point_id_missing");
                continue;
            }

            if (!Lookup.TryGetValue(declaration.Id, out var descriptor))
            {
                diagnostics.Add($"extension_point_unknown:{declaration.Id}");
                continue;
            }

            if (descriptor.Kind != declaration.Kind)
                diagnostics.Add($"extension_point_kind_mismatch:{declaration.Id}");
            if (!seen.TryAdd(declaration.Id, declaration))
                diagnostics.Add($"extension_point_duplicate:{declaration.Id}");
            if (declaration.Priority is < -1000 or > 1000)
                diagnostics.Add($"extension_point_priority_invalid:{declaration.Id}");
            if (!string.IsNullOrWhiteSpace(declaration.Replaces) && !descriptor.AllowsOverride)
                diagnostics.Add($"extension_point_replace_unsupported:{declaration.Id}");
        }

        foreach (var declaration in seen.Values)
        {
            var conflicts = declaration.Conflicts.IsDefault
                ? ImmutableArray<string>.Empty
                : declaration.Conflicts;
            foreach (var conflict in conflicts)
            {
                if (string.IsNullOrWhiteSpace(conflict))
                {
                    diagnostics.Add($"extension_point_conflict_invalid:{declaration.Id}");
                    continue;
                }

                if (seen.ContainsKey(conflict))
                    diagnostics.Add($"extension_point_conflict:{declaration.Id}:{conflict}");
            }
        }

        return diagnostics.Count == 0
            ? new PluginExtensionPointAdmissionResult(true, "admitted", ImmutableArray<string>.Empty)
            : new PluginExtensionPointAdmissionResult(false, "denied", diagnostics.ToImmutableArray());
    }

    private static ImmutableArray<PluginExtensionPointDescriptor> CreateAll() =>
    [
        new("provider.java", PluginExtensionPointKind.Provider, "Provides Java runtime discovery or resolution.", AllowsMultiple: true, AllowsOverride: false),
        new("provider.serverCore", PluginExtensionPointKind.Provider, "Provides server core discovery or download resolution.", AllowsMultiple: true, AllowsOverride: false),
        new("provider.backup", PluginExtensionPointKind.Provider, "Provides backup storage or execution behavior.", AllowsMultiple: true, AllowsOverride: false),
        new("provider.processLauncher", PluginExtensionPointKind.Provider, "Provides process launch behavior through the formal launcher boundary.", AllowsMultiple: true, AllowsOverride: false),
        new("registry.template", PluginExtensionPointKind.Registry, "Contributes instance templates.", AllowsMultiple: true, AllowsOverride: false),
        new("registry.serverCore", PluginExtensionPointKind.Registry, "Contributes server core catalog entries.", AllowsMultiple: true, AllowsOverride: false),
        new("registry.icon", PluginExtensionPointKind.Registry, "Contributes icon resources.", AllowsMultiple: true, AllowsOverride: false),
        new("registry.formatter", PluginExtensionPointKind.Registry, "Contributes display formatters.", AllowsMultiple: true, AllowsOverride: false),
        new("registry.command", PluginExtensionPointKind.Registry, "Contributes user-facing commands.", AllowsMultiple: true, AllowsOverride: false),
        new("pipeline.instance.create", PluginExtensionPointKind.Pipeline, "Participates in instance creation pipeline stages.", AllowsMultiple: true, AllowsOverride: false),
        new("hook.instance.lifecycle", PluginExtensionPointKind.Hook, "Observes documented instance lifecycle hooks.", AllowsMultiple: true, AllowsOverride: false),
        new("override.process.launcher", PluginExtensionPointKind.Override, "Overrides process launch behavior with fallback support.", AllowsMultiple: false, AllowsOverride: true),
        new("ui.panel", PluginExtensionPointKind.Ui, "Contributes client-rendered UI panels.", AllowsMultiple: true, AllowsOverride: false),
        new("event.daemon", PluginExtensionPointKind.Event, "Publishes or consumes daemon extension protocol events.", AllowsMultiple: true, AllowsOverride: false),
        new("command.daemon", PluginExtensionPointKind.Command, "Contributes daemon-backed extension protocol commands.", AllowsMultiple: true, AllowsOverride: false),
    ];
}
