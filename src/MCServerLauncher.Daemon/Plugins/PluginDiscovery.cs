using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
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
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase,
            "MCServerLauncher.Daemon.API",
            "MCServerLauncher.Common",
            "RustyOptions",
            "Microsoft.Extensions.Logging.Abstractions",
            "Microsoft.Extensions.DependencyInjection.Abstractions");

    private static readonly ImmutableHashSet<string> ForbiddenAssemblies =
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase,
            "MCServerLauncher.Daemon",
            "TouchSocket",
            "TouchSocket.Core",
            "TouchSocket.Http",
            "MessagePipe",
            "Serilog");

    internal static void ValidateBundle(PluginManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var bundleDirectory = Path.GetFullPath(manifest.BundleDirectory);
        var dllPaths = Directory.EnumerateFiles(bundleDirectory, "*.dll", SearchOption.AllDirectories)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();
        foreach (var path in dllPaths)
            ValidateAssemblyFileIdentity(
                path,
                manifest.Identity.Id,
                rejectSharedCopy: true,
                bundleDirectory: bundleDirectory);

        AssemblyDependencyResolver resolver;
        try
        {
            resolver = new AssemblyDependencyResolver(manifest.EntryAssemblyPath);
        }
        catch (Exception exception) when (exception is ArgumentException or FileLoadException or IOException)
        {
            throw new PluginAssemblyException(
                "assembly_invalid",
                $"Plugin bundle '{manifest.Identity.Id}' has an invalid dependency graph.",
                exception);
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

                    var resolvedPath = resolver.ResolveAssemblyToPath(new AssemblyName(referenceName));
                    if (resolvedPath is not null)
                    {
                        var fullResolvedPath = Path.GetFullPath(resolvedPath);
                        ValidateAssemblyFileIdentity(
                            fullResolvedPath,
                            manifest.Identity.Id,
                            rejectSharedCopy: IsWithinBundle(bundleDirectory, fullResolvedPath),
                            bundleDirectory: bundleDirectory);
                        pending.Enqueue(fullResolvedPath);
                    }
                }
            }
            catch (PluginAssemblyException)
            {
                throw;
            }
            catch (Exception exception) when (exception is ArgumentException or BadImageFormatException or FileLoadException or IOException or InvalidOperationException)
            {
                throw new PluginAssemblyException("assembly_invalid", $"Plugin assembly '{Path.GetFileName(path)}' is invalid.", exception);
            }
        }
    }

    internal static bool IsShared(string? assemblyName) => assemblyName is not null && SharedAssemblies.Contains(assemblyName);

    private static bool IsForbidden(string assemblyName) =>
        ForbiddenAssemblies.Contains(assemblyName) ||
        assemblyName.StartsWith("TouchSocket.", StringComparison.OrdinalIgnoreCase) ||
        assemblyName.StartsWith("Serilog.", StringComparison.OrdinalIgnoreCase) ||
        assemblyName.StartsWith("MessagePipe.", StringComparison.OrdinalIgnoreCase);

    private static void ValidateAssemblyFileIdentity(
        string path,
        string pluginId,
        bool rejectSharedCopy,
        string bundleDirectory)
    {
        var fileName = Path.GetFileNameWithoutExtension(path);
        ValidateAssemblyName(fileName, pluginId, path, rejectSharedCopy, bundleDirectory);

        var definedName = TryReadAssemblyDefinitionName(path);
        if (definedName is not null)
            ValidateAssemblyName(definedName, pluginId, path, rejectSharedCopy, bundleDirectory);
    }

    private static void ValidateAssemblyName(
        string assemblyName,
        string pluginId,
        string path,
        bool rejectSharedCopy,
        string bundleDirectory)
    {
        if (rejectSharedCopy && SharedAssemblies.Contains(assemblyName))
        {
            throw new PluginAssemblyException(
                "shared_contract_duplicate",
                $"Plugin bundle '{pluginId}' contains a private copy of shared assembly '{assemblyName}'.");
        }

        if (IsForbidden(assemblyName))
        {
            throw new PluginAssemblyException(
                "forbidden_reference",
                $"Plugin assembly '{Path.GetRelativePath(bundleDirectory, path)}' uses forbidden assembly '{assemblyName}'.");
        }
    }

    private static string? TryReadAssemblyDefinitionName(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var peReader = new PEReader(stream);
            var metadata = peReader.GetMetadataReader();
            if (!metadata.IsAssembly)
                return null;
            return metadata.GetString(metadata.GetAssemblyDefinition().Name);
        }
        catch (BadImageFormatException)
        {
            return null;
        }
    }

    private static bool IsWithinBundle(string bundleDirectory, string candidatePath)
    {
        var relative = Path.GetRelativePath(bundleDirectory, Path.GetFullPath(candidatePath));
        return !Path.IsPathRooted(relative) &&
               !StringComparer.Ordinal.Equals(relative, "..") &&
               !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
               !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
    }
}

internal sealed class PluginAssemblyException(
    string code,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string Code { get; } = code;
}
