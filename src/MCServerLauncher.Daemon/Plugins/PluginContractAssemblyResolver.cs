using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using MCServerLauncher.Daemon.API.Plugins;
using NuGet.Versioning;

namespace MCServerLauncher.Daemon.Plugins;

internal sealed record PluginContractAdmissionFailure(
    string PluginId,
    string BundleDirectory,
    string Code,
    string Message);

internal sealed record PluginContractAdmissionResult(
    PluginContractAssemblyResolver Resolver,
    ImmutableArray<PluginManifest> Plugins,
    ImmutableArray<PluginContractAdmissionFailure> Failures);

internal sealed class PluginContractAssemblyResolver
{
    private readonly Dictionary<string, SharedContractAssembly> _contracts;
    private readonly Dictionary<string, ImmutableHashSet<string>> _declaredByPlugin;

    private PluginContractAssemblyResolver(
        Dictionary<string, SharedContractAssembly> contracts,
        Dictionary<string, ImmutableHashSet<string>> declaredByPlugin)
    {
        _contracts = contracts;
        _declaredByPlugin = declaredByPlugin;
    }

    internal static PluginContractAdmissionResult Create(ImmutableArray<PluginManifest> manifests)
    {
        var contracts = new Dictionary<string, SharedContractAssembly>(StringComparer.Ordinal);
        var declaredByPlugin = new Dictionary<string, ImmutableHashSet<string>>(StringComparer.Ordinal);
        var failures = new List<PluginContractAdmissionFailure>();
        var failedPlugins = new HashSet<string>(StringComparer.Ordinal);

        foreach (var manifest in manifests.OrderBy(static item => item.Identity.Id, StringComparer.Ordinal))
        {
            var declared = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
            foreach (var dependency in manifest.ContractDependencies)
            {
                declared.Add(dependency.AssemblyName);
                var path = Path.Combine(manifest.BundleDirectory, dependency.Assembly);
                if (!File.Exists(path))
                {
                    Fail(manifest, "contract_missing", $"Contract assembly '{dependency.Assembly}' does not exist.");
                    continue;
                }

                AssemblyName assemblyName;
                try
                {
                    assemblyName = AssemblyName.GetAssemblyName(path);
                }
                catch (Exception exception) when (exception is ArgumentException or BadImageFormatException or FileLoadException or IOException)
                {
                    Fail(manifest, "contract_invalid", $"Contract assembly '{dependency.Assembly}' is not a valid assembly: {exception.Message}");
                    continue;
                }

                if (!string.Equals(assemblyName.Name, dependency.AssemblyName, StringComparison.Ordinal))
                {
                    Fail(manifest, "contract_identity_mismatch", $"Contract assembly '{dependency.Assembly}' defines '{assemblyName.Name}' instead of '{dependency.AssemblyName}'.");
                    continue;
                }

                var version = assemblyName.Version is null
                    ? NuGetVersion.Parse("0.0.0")
                    : NuGetVersion.Parse(assemblyName.Version.ToString());
                if (!dependency.VersionRange.Satisfies(version))
                {
                    Fail(manifest, "contract_version_mismatch", $"Contract assembly '{dependency.Assembly}' version '{version.ToNormalizedString()}' does not satisfy '{dependency.NormalizedVersionRange}'.");
                    continue;
                }

                var hash = ComputeSha256(path);
                if (!string.Equals(hash, dependency.Sha256, StringComparison.Ordinal))
                {
                    Fail(manifest, "contract_hash_mismatch", $"Contract assembly '{dependency.Assembly}' SHA-256 does not match the manifest.");
                    continue;
                }

                var fullPath = Path.GetFullPath(path);
                if (contracts.TryGetValue(dependency.AssemblyName, out var existing))
                {
                    if (!string.Equals(existing.Sha256, hash, StringComparison.Ordinal) ||
                        !string.Equals(existing.AssemblyPath, fullPath, StringComparison.Ordinal) &&
                        !AssemblyName.ReferenceMatchesDefinition(existing.AssemblyName, assemblyName))
                    {
                        Fail(manifest, "contract_conflict", $"Contract assembly '{dependency.AssemblyName}' conflicts with an already admitted contract assembly.");
                    }

                    continue;
                }

                contracts.Add(
                    dependency.AssemblyName,
                    new SharedContractAssembly(dependency.AssemblyName, fullPath, assemblyName, hash));
            }

            declaredByPlugin[manifest.Identity.Id] = declared.ToImmutable();
        }

        foreach (var manifest in manifests.OrderBy(static item => item.Identity.Id, StringComparer.Ordinal))
        {
            if (failedPlugins.Contains(manifest.Identity.Id))
                continue;

            var declared = declaredByPlugin.TryGetValue(manifest.Identity.Id, out var declaredContracts)
                ? declaredContracts
                : ImmutableHashSet<string>.Empty;
            foreach (var path in Directory.EnumerateFiles(manifest.BundleDirectory, "*.dll", SearchOption.TopDirectoryOnly))
            {
                var assemblyName = Path.GetFileNameWithoutExtension(path);
                if (assemblyName is null || !contracts.ContainsKey(assemblyName) || declared.Contains(assemblyName))
                    continue;

                Fail(manifest, "contract_private_copy", $"Plugin bundle contains private copy '{Path.GetFileName(path)}' of shared contract assembly '{assemblyName}' without declaring it.");
            }
        }

        var accepted = manifests
            .Where(manifest => !failedPlugins.Contains(manifest.Identity.Id))
            .ToImmutableArray();
        return new PluginContractAdmissionResult(
            new PluginContractAssemblyResolver(contracts, declaredByPlugin),
            accepted,
            failures.OrderBy(static failure => failure.PluginId, StringComparer.Ordinal)
                .ThenBy(static failure => failure.Code, StringComparer.Ordinal)
                .ToImmutableArray());

        void Fail(PluginManifest manifest, string code, string message)
        {
            failedPlugins.Add(manifest.Identity.Id);
            failures.Add(new PluginContractAdmissionFailure(
                manifest.Identity.Id,
                manifest.BundleDirectory,
                code,
                message));
        }
    }

    internal bool TryResolve(PluginManifest manifest, AssemblyName requestedName, out Assembly? assembly)
    {
        assembly = null;
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(requestedName);
        if (requestedName.Name is null)
            return false;
        if (!_declaredByPlugin.TryGetValue(manifest.Identity.Id, out var declared) || !declared.Contains(requestedName.Name))
            return false;
        if (!_contracts.TryGetValue(requestedName.Name, out var contract))
            return false;
        if (requestedName.Version is not null &&
            contract.AssemblyName.Version is not null &&
            requestedName.Version != contract.AssemblyName.Version)
        {
            return false;
        }

        assembly = contract.Load();
        return true;
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private sealed class SharedContractAssembly(
        string name,
        string assemblyPath,
        AssemblyName assemblyName,
        string sha256)
    {
        private readonly object _gate = new();
        private readonly PluginSharedContractLoadContext _loadContext = new(name, assemblyPath);
        private Assembly? _assembly;

        internal string AssemblyPath { get; } = assemblyPath;

        internal AssemblyName AssemblyName { get; } = assemblyName;

        internal string Sha256 { get; } = sha256;

        [UnconditionalSuppressMessage(
            "Trimming",
            "IL2026",
            Justification = "The daemon plugin product is an untrimmed JIT host; declared Contracts assemblies are loaded intentionally at startup.")]
        internal Assembly Load()
        {
            lock (_gate)
                return _assembly ??= _loadContext.LoadFromAssemblyPath(AssemblyPath);
        }
    }

    private sealed class PluginSharedContractLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;

        internal PluginSharedContractLoadContext(string name, string assemblyPath)
            : base($"MCServerLauncher.Plugin.Contracts.{name}", isCollectible: false)
        {
            _resolver = new AssemblyDependencyResolver(assemblyPath);
        }

        [UnconditionalSuppressMessage(
            "Trimming",
            "IL2026",
            Justification = "The daemon plugin product is an untrimmed JIT host; declared Contracts assembly dependencies are loaded intentionally at startup.")]
        protected override Assembly? Load(AssemblyName assemblyName)
        {
            ArgumentNullException.ThrowIfNull(assemblyName);
            if (PluginAssemblyPolicy.IsShared(assemblyName.Name))
                return PluginLoadContext.ResolveSharedAssembly(assemblyName.Name!);

            var path = _resolver.ResolveAssemblyToPath(assemblyName);
            return path is null ? null : LoadFromAssemblyPath(path);
        }
    }
}
