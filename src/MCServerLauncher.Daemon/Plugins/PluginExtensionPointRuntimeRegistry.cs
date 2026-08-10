using System.Collections.Immutable;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Plugins;
using RustyOptions;

namespace MCServerLauncher.Daemon.Plugins;

internal sealed record PluginExtensionPointRuntimeRegistration(
    string ExtensionPointId,
    PluginIdentity Owner,
    PluginExtensionPointKind Kind,
    int Priority,
    string? Replaces,
    string? DefaultFor,
    Type ContractType,
    object Implementation);

internal sealed record PluginExtensionPointContribution<TContribution>(
    string ExtensionPointId,
    PluginIdentity Owner,
    int Priority,
    TContribution Value);

internal sealed class PluginExtensionPointRuntimeRegistry
{
    private static readonly IReadOnlyDictionary<string, Type> ContractTypes = new Dictionary<string, Type>(StringComparer.Ordinal)
    {
        ["provider.java"] = typeof(IPluginJavaRuntimeProvider),
        ["provider.serverCore"] = typeof(IPluginServerCoreProvider),
        ["provider.backup"] = typeof(IPluginBackupProvider),
        ["provider.processLauncher"] = typeof(IPluginProcessLauncherProvider),
        ["registry.template"] = typeof(IPluginInstanceTemplateRegistry),
        ["registry.serverCore"] = typeof(IPluginServerCoreRegistry),
        ["registry.icon"] = typeof(IPluginIconRegistry),
        ["registry.formatter"] = typeof(IPluginFormatterRegistry),
        ["registry.command"] = typeof(IPluginCommandRegistry),
        ["pipeline.instance.create"] = typeof(IPluginInstanceCreatePipelineStage),
        ["hook.instance.lifecycle"] = typeof(IPluginInstanceLifecycleHook),
        ["override.process.launcher"] = typeof(IPluginProcessLauncherOverride)
    };

    private readonly object _gate = new();
    private readonly Dictionary<string, List<PluginExtensionPointRuntimeRegistration>> _registrations = new(StringComparer.Ordinal);

    internal IPluginExtensionPointRegistrar CreateRegistrar(PluginManifest manifest, PluginErrorFactory errors) =>
        new Registrar(this, manifest, errors);

    internal ImmutableArray<PluginExtensionPointRuntimeRegistration> List(string extensionPointId)
    {
        lock (_gate)
        {
            return _registrations.TryGetValue(extensionPointId, out var registrations)
                ? registrations
                    .OrderByDescending(static registration => registration.Priority)
                    .ThenBy(static registration => registration.Owner.Id, StringComparer.Ordinal)
                    .ToImmutableArray()
                : ImmutableArray<PluginExtensionPointRuntimeRegistration>.Empty;
        }
    }

    internal void Remove(string pluginId)
    {
        lock (_gate)
        {
            foreach (var extensionPointId in _registrations.Keys.ToArray())
            {
                _registrations[extensionPointId].RemoveAll(registration =>
                    StringComparer.Ordinal.Equals(registration.Owner.Id, pluginId));
                if (_registrations[extensionPointId].Count == 0)
                    _registrations.Remove(extensionPointId);
            }
        }
    }

    internal ValueTask<Result<ImmutableArray<PluginExtensionPointContribution<PluginInstanceTemplateContribution>>, DaemonError>> ListInstanceTemplatesAsync(
        CancellationToken cancellationToken) =>
        ListRegistryContributionsAsync<IPluginInstanceTemplateRegistry, PluginInstanceTemplateContribution>(
            "registry.template",
            static (registry, token) => registry.ListAsync(token),
            static contribution => contribution.Id,
            static contribution => string.IsNullOrWhiteSpace(contribution.Id) ||
                                   string.IsNullOrWhiteSpace(contribution.DisplayName) ||
                                   string.IsNullOrWhiteSpace(contribution.InstanceType)
                ? "id, displayName, and instanceType are required"
                : null,
            cancellationToken);

    internal ValueTask<Result<ImmutableArray<PluginExtensionPointContribution<PluginServerCoreCatalogEntry>>, DaemonError>> ListServerCoreCatalogAsync(
        CancellationToken cancellationToken) =>
        ListRegistryContributionsAsync<IPluginServerCoreRegistry, PluginServerCoreCatalogEntry>(
            "registry.serverCore",
            static (registry, token) => registry.ListAsync(token),
            static contribution => contribution.Id,
            static contribution => string.IsNullOrWhiteSpace(contribution.Id) ||
                                   string.IsNullOrWhiteSpace(contribution.Family) ||
                                   string.IsNullOrWhiteSpace(contribution.Version) ||
                                   string.IsNullOrWhiteSpace(contribution.DisplayName)
                ? "id, family, version, and displayName are required"
                : null,
            cancellationToken);

    internal ValueTask<Result<ImmutableArray<PluginExtensionPointContribution<PluginIconContribution>>, DaemonError>> ListIconsAsync(
        CancellationToken cancellationToken) =>
        ListRegistryContributionsAsync<IPluginIconRegistry, PluginIconContribution>(
            "registry.icon",
            static (registry, token) => registry.ListAsync(token),
            static contribution => contribution.Id,
            static contribution => string.IsNullOrWhiteSpace(contribution.Id) ||
                                   string.IsNullOrWhiteSpace(contribution.DisplayName) ||
                                   string.IsNullOrWhiteSpace(contribution.ResourcePath) ||
                                   string.IsNullOrWhiteSpace(contribution.MimeType)
                ? "id, displayName, resourcePath, and mimeType are required"
                : null,
            cancellationToken);

    internal ValueTask<Result<ImmutableArray<PluginExtensionPointContribution<PluginFormatterContribution>>, DaemonError>> ListFormattersAsync(
        CancellationToken cancellationToken) =>
        ListRegistryContributionsAsync<IPluginFormatterRegistry, PluginFormatterContribution>(
            "registry.formatter",
            static (registry, token) => registry.ListAsync(token),
            static contribution => contribution.Id,
            static contribution => string.IsNullOrWhiteSpace(contribution.Id) ||
                                   string.IsNullOrWhiteSpace(contribution.DisplayName) ||
                                   string.IsNullOrWhiteSpace(contribution.AppliesTo)
                ? "id, displayName, and appliesTo are required"
                : null,
            cancellationToken);

    internal ValueTask<Result<ImmutableArray<PluginExtensionPointContribution<PluginCommandContribution>>, DaemonError>> ListCommandsAsync(
        CancellationToken cancellationToken) =>
        ListRegistryContributionsAsync<IPluginCommandRegistry, PluginCommandContribution>(
            "registry.command",
            static (registry, token) => registry.ListAsync(token),
            static contribution => contribution.Id,
            static contribution => string.IsNullOrWhiteSpace(contribution.Id) ||
                                   string.IsNullOrWhiteSpace(contribution.DisplayName) ||
                                   string.IsNullOrWhiteSpace(contribution.Target)
                ? "id, displayName, and target are required"
                : null,
            cancellationToken);

    private async ValueTask<Result<ImmutableArray<PluginExtensionPointContribution<TContribution>>, DaemonError>> ListRegistryContributionsAsync<TContract, TContribution>(
        string extensionPointId,
        Func<TContract, CancellationToken, ValueTask<Result<IReadOnlyList<TContribution>, DaemonError>>> listAsync,
        Func<TContribution, string?> getId,
        Func<TContribution, string?> validate,
        CancellationToken cancellationToken)
        where TContract : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        var registrations = List(extensionPointId);
        if (registrations.Length == 0)
        {
            return Result.Ok<ImmutableArray<PluginExtensionPointContribution<TContribution>>, DaemonError>(
                ImmutableArray<PluginExtensionPointContribution<TContribution>>.Empty);
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var builder = ImmutableArray.CreateBuilder<PluginExtensionPointContribution<TContribution>>();
        foreach (var registration in registrations)
        {
            if (registration.Implementation is not TContract contract)
                continue;

            var result = await listAsync(contract, cancellationToken).ConfigureAwait(false);
            if (result.IsErr(out var error))
                return Result.Err<ImmutableArray<PluginExtensionPointContribution<TContribution>>, DaemonError>(error!);

            var contributions = result.Unwrap();
            if (contributions is null)
            {
                return Result.Err<ImmutableArray<PluginExtensionPointContribution<TContribution>>, DaemonError>(
                    new InternalDaemonError("extension_point_registry_invalid", $"Plugin '{registration.Owner.Id}' returned null contributions for '{extensionPointId}'."));
            }

            foreach (var contribution in contributions)
            {
                var invalidReason = validate(contribution);
                if (invalidReason is not null)
                {
                    return Result.Err<ImmutableArray<PluginExtensionPointContribution<TContribution>>, DaemonError>(
                        new InternalDaemonError("extension_point_registry_invalid", $"Plugin '{registration.Owner.Id}' returned invalid '{extensionPointId}' contribution: {invalidReason}."));
                }

                var id = getId(contribution);
                if (id is null || !seen.Add(id))
                    continue;

                builder.Add(new PluginExtensionPointContribution<TContribution>(
                    extensionPointId,
                    registration.Owner,
                    registration.Priority,
                    contribution));
            }
        }

        return Result.Ok<ImmutableArray<PluginExtensionPointContribution<TContribution>>, DaemonError>(builder.ToImmutable());
    }

    private Result<Unit, DaemonError> Register<TContract>(
        PluginManifest manifest,
        PluginErrorFactory errors,
        string extensionPointId,
        TContract implementation)
        where TContract : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionPointId);
        ArgumentNullException.ThrowIfNull(implementation);

        if (!ContractTypes.TryGetValue(extensionPointId, out var expectedContract))
        {
            return PluginResult.Fail(errors.Create(
                "extension_point_runtime_unsupported",
                $"Extension point '{extensionPointId}' has no runtime registrar contract."));
        }

        var declaration = manifest.ExtensionPoints.FirstOrDefault(point =>
            StringComparer.Ordinal.Equals(point.Id, extensionPointId));
        if (declaration is null)
        {
            return PluginResult.Fail(errors.Create(
                "extension_point_declaration_required",
                $"Plugin '{manifest.Identity.Id}' must declare extension point '{extensionPointId}' before registering its runtime implementation."));
        }

        if (!expectedContract.IsInstanceOfType(implementation))
        {
            return PluginResult.Fail(errors.Create(
                "extension_point_contract_mismatch",
                $"Extension point '{extensionPointId}' requires contract '{expectedContract.FullName}'."));
        }

        if (!ExtensionPointCatalog.TryGet(extensionPointId, out var descriptor))
        {
            return PluginResult.Fail(errors.Create(
                "extension_point_unknown",
                $"Extension point '{extensionPointId}' is not known."));
        }

        lock (_gate)
        {
            var registrations = _registrations.GetValueOrDefault(extensionPointId);
            if (registrations is null)
            {
                registrations = [];
                _registrations[extensionPointId] = registrations;
            }

            if (!descriptor.AllowsMultiple && registrations.Any(registration =>
                    !StringComparer.Ordinal.Equals(registration.Owner.Id, manifest.Identity.Id)))
            {
                return PluginResult.Fail(errors.Create(
                    "extension_point_duplicate",
                    $"Extension point '{extensionPointId}' allows only one runtime registration."));
            }

            registrations.RemoveAll(registration =>
                StringComparer.Ordinal.Equals(registration.Owner.Id, manifest.Identity.Id));
            registrations.Add(new PluginExtensionPointRuntimeRegistration(
                extensionPointId,
                manifest.Identity,
                declaration.Kind,
                declaration.Priority,
                declaration.Replaces,
                declaration.DefaultFor,
                expectedContract,
                implementation));
        }

        return PluginResult.Ok();
    }

    private sealed class Registrar(
        PluginExtensionPointRuntimeRegistry registry,
        PluginManifest manifest,
        PluginErrorFactory errors) : IPluginExtensionPointRegistrar
    {
        public Result<Unit, DaemonError> Register<TContract>(string extensionPointId, TContract implementation)
            where TContract : class =>
            registry.Register(manifest, errors, extensionPointId, implementation);
    }
}
