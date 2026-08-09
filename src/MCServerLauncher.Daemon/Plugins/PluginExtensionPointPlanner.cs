using System.Collections.Immutable;
using MCServerLauncher.Daemon.API.Plugins;

namespace MCServerLauncher.Daemon.Plugins;

internal sealed record PluginExtensionPointAdmissionFailure(
    string PluginId,
    string BundleDirectory,
    string Code,
    string Message);

internal sealed record PluginExtensionPointAdmissionResult(
    ImmutableArray<PluginManifest> Plugins,
    ImmutableArray<PluginExtensionPointAdmissionFailure> Failures);

internal static class PluginExtensionPointPlanner
{
    internal static PluginExtensionPointAdmissionResult Plan(ImmutableArray<PluginManifest> manifests)
    {
        if (manifests.IsDefaultOrEmpty)
        {
            return new PluginExtensionPointAdmissionResult(
                ImmutableArray<PluginManifest>.Empty,
                ImmutableArray<PluginExtensionPointAdmissionFailure>.Empty);
        }

        Dictionary<string, PluginExtensionPointAdmissionFailure> failures = new(StringComparer.Ordinal);
        var declarations = manifests
            .SelectMany(static manifest => manifest.ExtensionPoints.Select(extensionPoint => new DeclaredExtensionPoint(manifest, extensionPoint)))
            .OrderBy(static declaration => declaration.Manifest.Identity.Id, StringComparer.Ordinal)
            .ThenBy(static declaration => declaration.ExtensionPoint.Id, StringComparer.Ordinal)
            .ToArray();

        var byPoint = declarations
            .GroupBy(static declaration => declaration.ExtensionPoint.Id, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.ToArray(), StringComparer.Ordinal);

        foreach (var group in byPoint.Values)
        {
            if (group.Length <= 1)
                continue;

            if (!ExtensionPointCatalog.TryGet(group[0].ExtensionPoint.Id, out var descriptor))
                continue;

            if (!descriptor.AllowsMultiple)
            {
                foreach (var declaration in group)
                {
                    Fail(
                        declaration.Manifest,
                        "extension_point_duplicate",
                        $"Extension point '{declaration.ExtensionPoint.Id}' allows only one provider, but multiple plugins declared it.");
                }
            }
        }

        foreach (var declaration in declarations)
        {
            var conflicts = declaration.ExtensionPoint.Conflicts.IsDefault
                ? ImmutableArray<string>.Empty
                : declaration.ExtensionPoint.Conflicts;
            foreach (var conflict in conflicts)
            {
                if (!byPoint.TryGetValue(conflict, out var conflictingDeclarations))
                    continue;

                Fail(
                    declaration.Manifest,
                    "extension_point_conflict",
                    $"Extension point '{declaration.ExtensionPoint.Id}' conflicts with declared extension point '{conflict}'.");
                foreach (var conflicting in conflictingDeclarations)
                {
                    Fail(
                        conflicting.Manifest,
                        "extension_point_conflict",
                        $"Extension point '{conflicting.ExtensionPoint.Id}' conflicts with declared extension point '{declaration.ExtensionPoint.Id}'.");
                }
            }
        }

        var accepted = manifests
            .Where(manifest => !failures.ContainsKey(manifest.Identity.Id))
            .ToImmutableArray();
        return new PluginExtensionPointAdmissionResult(
            accepted,
            failures.Values
                .OrderBy(static failure => failure.PluginId, StringComparer.Ordinal)
                .ThenBy(static failure => failure.Code, StringComparer.Ordinal)
                .ToImmutableArray());

        void Fail(PluginManifest manifest, string code, string message)
        {
            failures.TryAdd(
                manifest.Identity.Id,
                new PluginExtensionPointAdmissionFailure(
                    manifest.Identity.Id,
                    manifest.BundleDirectory,
                    code,
                    message));
        }
    }

    private sealed record DeclaredExtensionPoint(PluginManifest Manifest, PluginManifestExtensionPoint ExtensionPoint);
}
