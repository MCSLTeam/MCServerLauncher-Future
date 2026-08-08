using System.Collections.Immutable;

namespace MCServerLauncher.Daemon.Plugins;

internal sealed record PluginDependencyAdmissionFailure(
    string PluginId,
    string BundleDirectory,
    string Code,
    string Message);

internal sealed record PluginDependencyAdmissionResult(
    ImmutableArray<PluginManifest> OrderedPlugins,
    ImmutableArray<PluginDependencyAdmissionFailure> Failures);

internal static class PluginDependencyPlanner
{
    internal static PluginDependencyAdmissionResult Plan(ImmutableArray<PluginManifest> manifests)
    {
        if (manifests.IsDefaultOrEmpty)
            return new PluginDependencyAdmissionResult(
                ImmutableArray<PluginManifest>.Empty,
                ImmutableArray<PluginDependencyAdmissionFailure>.Empty);

        var byId = manifests.ToDictionary(static manifest => manifest.Identity.Id, StringComparer.Ordinal);
        var failures = new Dictionary<string, PluginDependencyAdmissionFailure>(StringComparer.Ordinal);
        foreach (var manifest in manifests.OrderBy(static item => item.Identity.Id, StringComparer.Ordinal))
        {
            foreach (var dependency in manifest.PluginDependencies)
            {
                if (!byId.TryGetValue(dependency.Id, out var provider))
                {
                    failures[manifest.Identity.Id] = new PluginDependencyAdmissionFailure(
                        manifest.Identity.Id,
                        manifest.BundleDirectory,
                        "dependency_missing",
                        $"Plugin '{manifest.Identity.Id}' requires missing plugin '{dependency.Id}'.");
                    break;
                }

                if (!dependency.VersionRange.Satisfies(provider.Version))
                {
                    failures[manifest.Identity.Id] = new PluginDependencyAdmissionFailure(
                        manifest.Identity.Id,
                        manifest.BundleDirectory,
                        "dependency_version_unsupported",
                        $"Plugin '{manifest.Identity.Id}' requires plugin '{dependency.Id}' version range '{dependency.NormalizedVersionRange}', but discovered version '{provider.Identity.Version}'.");
                    break;
                }
            }
        }

        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var manifest in manifests.OrderBy(static item => item.Identity.Id, StringComparer.Ordinal))
            {
                if (failures.ContainsKey(manifest.Identity.Id))
                    continue;

                var blockedDependency = manifest.PluginDependencies
                    .FirstOrDefault(dependency => failures.ContainsKey(dependency.Id));
                if (blockedDependency is null)
                    continue;

                failures[manifest.Identity.Id] = new PluginDependencyAdmissionFailure(
                    manifest.Identity.Id,
                    manifest.BundleDirectory,
                    "dependency_blocked",
                    $"Plugin '{manifest.Identity.Id}' depends on skipped plugin '{blockedDependency.Id}'.");
                changed = true;
            }
        }

        var candidates = manifests
            .Where(manifest => !failures.ContainsKey(manifest.Identity.Id))
            .OrderBy(static manifest => manifest.Identity.Id, StringComparer.Ordinal)
            .ToArray();
        var candidateIds = candidates.Select(static manifest => manifest.Identity.Id).ToHashSet(StringComparer.Ordinal);
        var dependents = candidates.ToDictionary(
            static manifest => manifest.Identity.Id,
            static _ => new SortedSet<string>(StringComparer.Ordinal),
            StringComparer.Ordinal);
        var indegrees = candidates.ToDictionary(
            static manifest => manifest.Identity.Id,
            manifest => manifest.PluginDependencies.Count(dependency => candidateIds.Contains(dependency.Id)),
            StringComparer.Ordinal);

        foreach (var manifest in candidates)
        {
            foreach (var dependency in manifest.PluginDependencies.Where(dependency => candidateIds.Contains(dependency.Id)))
                dependents[dependency.Id].Add(manifest.Identity.Id);
        }

        var ready = new SortedSet<string>(
            indegrees.Where(static item => item.Value == 0).Select(static item => item.Key),
            StringComparer.Ordinal);
        var ordered = ImmutableArray.CreateBuilder<PluginManifest>(candidates.Length);
        while (ready.Count > 0)
        {
            var id = ready.Min!;
            ready.Remove(id);
            ordered.Add(byId[id]);

            foreach (var dependentId in dependents[id])
            {
                indegrees[dependentId]--;
                if (indegrees[dependentId] == 0)
                    ready.Add(dependentId);
            }
        }

        if (ordered.Count != candidates.Length)
        {
            var orderedIds = ordered.Select(static manifest => manifest.Identity.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var manifest in candidates.Where(manifest => !orderedIds.Contains(manifest.Identity.Id)))
            {
                failures[manifest.Identity.Id] = new PluginDependencyAdmissionFailure(
                    manifest.Identity.Id,
                    manifest.BundleDirectory,
                    "dependency_cycle",
                    $"Plugin '{manifest.Identity.Id}' is part of or depends on a cyclic plugin dependency graph.");
            }
        }

        var surviving = ordered
            .Where(manifest => !failures.ContainsKey(manifest.Identity.Id))
            .ToImmutableArray();
        var orderedFailures = failures.Values
            .OrderBy(static failure => failure.PluginId, StringComparer.Ordinal)
            .ThenBy(static failure => failure.Code, StringComparer.Ordinal)
            .ToImmutableArray();

        return new PluginDependencyAdmissionResult(surviving, orderedFailures);
    }
}
