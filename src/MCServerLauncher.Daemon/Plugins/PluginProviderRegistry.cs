using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Plugins;
using RustyOptions;

namespace MCServerLauncher.Daemon.Plugins;

internal sealed class PluginProviderRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Dictionary<Type, object>> _exportsByPlugin = new(StringComparer.Ordinal);

    internal IPluginProviderRegistry CreateExporter(PluginManifest manifest, PluginErrorFactory errors) =>
        new Exporter(this, manifest, errors);

    internal IPluginProviderImports CreateImports(PluginManifest manifest, PluginErrorFactory errors) =>
        new Imports(this, manifest, errors);

    internal void Remove(string pluginId)
    {
        lock (_gate)
            _exportsByPlugin.Remove(pluginId);
    }

    private Result<Unit, DaemonError> Export<TContract>(
        PluginManifest manifest,
        PluginErrorFactory errors,
        TContract implementation)
        where TContract : class
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(errors);
        if (implementation is null)
            return PluginResult.Fail(errors.Create("plugin_provider_null", "A plugin provider export cannot be null."));

        var contractType = typeof(TContract);
        var validation = ValidateContractType(manifest, errors, contractType);
        if (validation.IsErr(out var error))
            return Result.Err<Unit, DaemonError>(error!);

        lock (_gate)
        {
            if (!_exportsByPlugin.TryGetValue(manifest.Identity.Id, out var exports))
            {
                exports = new Dictionary<Type, object>();
                _exportsByPlugin.Add(manifest.Identity.Id, exports);
            }

            if (exports.ContainsKey(contractType))
            {
                return PluginResult.Fail(errors.Create(
                    "plugin_provider_duplicate",
                    $"Plugin '{manifest.Identity.Id}' already exported contract '{contractType.FullName}'."));
            }

            exports.Add(contractType, implementation);
            return PluginResult.Ok();
        }
    }

    private Result<TContract, DaemonError> Import<TContract>(
        PluginManifest manifest,
        PluginErrorFactory errors,
        string pluginId)
        where TContract : class
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(errors);
        if (string.IsNullOrWhiteSpace(pluginId))
            return PluginResult.Fail<TContract>(errors.Create("plugin_import_invalid", "A provider plugin id is required."));

        var dependency = manifest.PluginDependencies.FirstOrDefault(item => string.Equals(item.Id, pluginId, StringComparison.Ordinal));
        if (dependency is null)
        {
            return PluginResult.Fail<TContract>(errors.Create(
                "plugin_dependency_required",
                $"Plugin '{manifest.Identity.Id}' must declare plugin dependency '{pluginId}' before importing from it."));
        }

        var contractType = typeof(TContract);
        var validation = ValidateContractType(manifest, errors, contractType);
        if (validation.IsErr(out var error))
            return Result.Err<TContract, DaemonError>(error!);

        lock (_gate)
        {
            if (!_exportsByPlugin.TryGetValue(pluginId, out var exports) ||
                !exports.TryGetValue(contractType, out var export))
            {
                return PluginResult.Fail<TContract>(errors.Create(
                    "plugin_provider_missing",
                    $"Plugin dependency '{pluginId}' has not exported contract '{contractType.FullName}'."));
            }

            return PluginResult.Ok((TContract)export);
        }
    }

    private static Result<Unit, DaemonError> ValidateContractType(
        PluginManifest manifest,
        PluginErrorFactory errors,
        Type contractType)
    {
        if (!contractType.IsInterface)
        {
            return PluginResult.Fail(errors.Create(
                "plugin_contract_invalid",
                $"Plugin provider contract '{contractType.FullName}' must be an interface."));
        }

        var assemblyName = contractType.Assembly.GetName().Name;
        if (string.IsNullOrWhiteSpace(assemblyName) ||
            !manifest.ContractDependencies.Any(dependency =>
                string.Equals(dependency.AssemblyName, assemblyName, StringComparison.Ordinal)))
        {
            return PluginResult.Fail(errors.Create(
                "plugin_contract_undeclared",
                $"Plugin '{manifest.Identity.Id}' must declare contract assembly '{assemblyName}' before using '{contractType.FullName}'."));
        }

        return PluginResult.Ok();
    }

    private sealed class Exporter(
        PluginProviderRegistry registry,
        PluginManifest manifest,
        PluginErrorFactory errors) : IPluginProviderRegistry
    {
        public Result<Unit, DaemonError> Export<TContract>(TContract implementation)
            where TContract : class =>
            registry.Export(manifest, errors, implementation);
    }

    private sealed class Imports(
        PluginProviderRegistry registry,
        PluginManifest manifest,
        PluginErrorFactory errors) : IPluginProviderImports
    {
        public Result<TContract, DaemonError> Import<TContract>(string pluginId)
            where TContract : class =>
            registry.Import<TContract>(manifest, errors, pluginId);
    }
}
