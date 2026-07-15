using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using NuGet.Versioning;

namespace MCServerLauncher.Daemon.Plugins;

internal sealed class PluginDiscovery(string hostApiVersion)
{
    private readonly string _hostApiVersion = hostApiVersion ?? throw new ArgumentNullException(nameof(hostApiVersion));

    internal PluginDiscoveryResult Discover(string pluginsRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginsRoot);
        var fullRoot = Path.GetFullPath(pluginsRoot);
        if (!Directory.Exists(fullRoot))
            return new PluginDiscoveryResult(ImmutableArray<PluginManifest>.Empty, ImmutableArray<PluginDiscoveryFailure>.Empty);

        var candidates = new List<PluginManifest>();
        var failures = new List<PluginDiscoveryFailure>();
        foreach (var directory in Directory.EnumerateDirectories(fullRoot).OrderBy(static path => path, StringComparer.Ordinal))
        {
            try
            {
                var manifest = PluginManifestReader.ReadAndValidate(directory, _hostApiVersion);
                PluginAssemblyPolicy.ValidateBundle(manifest);
                candidates.Add(manifest);
            }
            catch (PluginManifestException exception)
            {
                failures.Add(new PluginDiscoveryFailure(directory, exception.Code, exception.Message, exception));
            }
            catch (PluginAssemblyException exception)
            {
                failures.Add(new PluginDiscoveryFailure(directory, exception.Code, exception.Message, exception));
            }
            catch (Exception exception)
            {
                failures.Add(new PluginDiscoveryFailure(directory, "discovery_failed", "The plugin bundle could not be discovered.", exception));
            }
        }

        foreach (var duplicateGroup in candidates.GroupBy(static candidate => candidate.Identity.Id, StringComparer.Ordinal)
                     .Where(static group => group.Count() > 1))
        {
            foreach (var candidate in duplicateGroup)
            {
                failures.Add(new PluginDiscoveryFailure(
                    candidate.BundleDirectory,
                    "duplicate_id",
                    $"Plugin id '{candidate.Identity.Id}' is declared by more than one bundle."));
                candidates.Remove(candidate);
            }
        }

        var orderedCandidates = candidates
            .OrderBy(static candidate => candidate.Identity.Id, StringComparer.Ordinal)
            .ThenBy(static candidate => candidate.Identity.Version, StringComparer.Ordinal)
            .ToImmutableArray();
        var orderedFailures = failures
            .OrderBy(static failure => failure.BundleDirectory, StringComparer.Ordinal)
            .ThenBy(static failure => failure.Code, StringComparer.Ordinal)
            .ToImmutableArray();
        return new PluginDiscoveryResult(orderedCandidates, orderedFailures);
    }
}

internal static class PluginAssemblyPolicy
{
    private static readonly ImmutableHashSet<string> SharedAssemblies =
        ImmutableHashSet.Create(StringComparer.Ordinal,
            "MCServerLauncher.Daemon.API",
            "MCServerLauncher.Common",
            "RustyOptions",
            "Microsoft.Extensions.Logging.Abstractions");

    private static readonly ImmutableHashSet<string> ForbiddenAssemblies =
        ImmutableHashSet.Create(StringComparer.Ordinal,
            "MCServerLauncher.Daemon",
            "TouchSocket",
            "TouchSocket.Core",
            "TouchSocket.Http",
            "MessagePipe",
            "Serilog");

    internal static void ValidateBundle(PluginManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var dllPaths = Directory.EnumerateFiles(manifest.BundleDirectory, "*.dll", SearchOption.TopDirectoryOnly)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();
        foreach (var path in dllPaths)
        {
            var assemblyName = Path.GetFileNameWithoutExtension(path);
            if (SharedAssemblies.Contains(assemblyName))
            {
                throw new PluginAssemblyException(
                    "shared_contract_duplicate",
                    $"Plugin bundle '{manifest.Identity.Id}' contains a private copy of shared assembly '{assemblyName}'.");
            }

            if (IsForbidden(assemblyName))
            {
                throw new PluginAssemblyException(
                    "forbidden_reference",
                    $"Plugin bundle '{manifest.Identity.Id}' contains forbidden assembly '{assemblyName}'.");
            }
        }

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<string>();
        pending.Enqueue(manifest.EntryAssemblyPath);
        while (pending.Count > 0)
        {
            var path = pending.Dequeue();
            if (!visited.Add(path))
                continue;

            try
            {
                using var stream = File.OpenRead(path);
                using var peReader = new PEReader(stream);
                var metadata = peReader.GetMetadataReader();
                foreach (var handle in metadata.AssemblyReferences)
                {
                    var reference = metadata.GetAssemblyReference(handle);
                    var referenceName = metadata.GetString(reference.Name);
                    if (IsForbidden(referenceName))
                    {
                        throw new PluginAssemblyException(
                            "forbidden_reference",
                            $"Plugin assembly '{Path.GetFileName(path)}' references forbidden assembly '{referenceName}'.");
                    }

                    var localPath = Path.Combine(manifest.BundleDirectory, referenceName + ".dll");
                    if (File.Exists(localPath))
                        pending.Enqueue(localPath);
                }
            }
            catch (PluginAssemblyException)
            {
                throw;
            }
            catch (Exception exception) when (exception is BadImageFormatException or IOException or InvalidOperationException)
            {
                throw new PluginAssemblyException("assembly_invalid", $"Plugin assembly '{Path.GetFileName(path)}' is invalid.", exception);
            }
        }
    }

    internal static bool IsShared(string? assemblyName) => assemblyName is not null && SharedAssemblies.Contains(assemblyName);

    private static bool IsForbidden(string assemblyName) =>
        ForbiddenAssemblies.Contains(assemblyName) ||
        assemblyName.StartsWith("TouchSocket.", StringComparison.Ordinal) ||
        assemblyName.StartsWith("Serilog.", StringComparison.Ordinal) ||
        assemblyName.StartsWith("MessagePipe.", StringComparison.Ordinal);
}

internal sealed class PluginAssemblyException(
    string code,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string Code { get; } = code;
}
