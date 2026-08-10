using MCServerLauncher.Daemon.API.Errors;
using RustyOptions;

namespace MCServerLauncher.Daemon.API.Plugins;

public sealed record PluginExtensionContributionDescriptor(
    string ExtensionPointId,
    string PluginId,
    int Priority,
    string? Replaces,
    string? DefaultFor,
    Type ContractType);

public interface IPluginExtensionPointRegistrar
{
    Result<Unit, DaemonError> Register<TContract>(string extensionPointId, TContract implementation)
        where TContract : class;
}

public sealed record PluginJavaRuntimeRequest(
    string? Version,
    string? Architecture,
    IReadOnlyDictionary<string, string>? Properties = null);

public sealed record PluginJavaRuntimeResolution(
    string DisplayName,
    string JavaHome,
    string? ExecutablePath,
    IReadOnlyDictionary<string, string>? Metadata = null);

public interface IPluginJavaRuntimeProvider
{
    ValueTask<Result<PluginJavaRuntimeResolution, DaemonError>> ResolveAsync(
        PluginJavaRuntimeRequest request,
        CancellationToken cancellationToken);
}

public sealed record PluginServerCoreRequest(
    string Family,
    string Version,
    string? Loader,
    IReadOnlyDictionary<string, string>? Properties = null);

public sealed record PluginServerCoreResolution(
    string Id,
    string DisplayName,
    Uri? DownloadUri,
    string? Sha256,
    IReadOnlyDictionary<string, string>? Metadata = null);

public interface IPluginServerCoreProvider
{
    ValueTask<Result<PluginServerCoreResolution, DaemonError>> ResolveAsync(
        PluginServerCoreRequest request,
        CancellationToken cancellationToken);
}

public sealed record PluginBackupProviderRequest(
    string InstanceId,
    string Operation,
    IReadOnlyDictionary<string, string>? Properties = null);

public sealed record PluginBackupProviderResolution(
    string ProviderId,
    string DisplayName,
    IReadOnlyDictionary<string, string>? Metadata = null);

public interface IPluginBackupProvider
{
    ValueTask<Result<PluginBackupProviderResolution, DaemonError>> ResolveAsync(
        PluginBackupProviderRequest request,
        CancellationToken cancellationToken);
}

public sealed record PluginProcessLaunchRequest(
    string InstanceId,
    string WorkingDirectory,
    IReadOnlyList<string> Command,
    IReadOnlyDictionary<string, string>? Environment = null);

public sealed record PluginProcessLaunchPlan(
    string Executable,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string>? Environment = null);

public interface IPluginProcessLauncherProvider
{
    ValueTask<Result<PluginProcessLaunchPlan, DaemonError>> CreatePlanAsync(
        PluginProcessLaunchRequest request,
        CancellationToken cancellationToken);
}

public sealed record PluginInstanceTemplateContribution(
    string Id,
    string DisplayName,
    string InstanceType,
    IReadOnlyDictionary<string, string>? Defaults = null);

public interface IPluginInstanceTemplateRegistry
{
    ValueTask<Result<IReadOnlyList<PluginInstanceTemplateContribution>, DaemonError>> ListAsync(
        CancellationToken cancellationToken);
}

public sealed record PluginServerCoreCatalogEntry(
    string Id,
    string Family,
    string Version,
    string DisplayName,
    IReadOnlyDictionary<string, string>? Metadata = null);

public interface IPluginServerCoreRegistry
{
    ValueTask<Result<IReadOnlyList<PluginServerCoreCatalogEntry>, DaemonError>> ListAsync(
        CancellationToken cancellationToken);
}

public sealed record PluginIconContribution(
    string Id,
    string DisplayName,
    string ResourcePath,
    string MimeType);

public interface IPluginIconRegistry
{
    ValueTask<Result<IReadOnlyList<PluginIconContribution>, DaemonError>> ListAsync(
        CancellationToken cancellationToken);
}

public sealed record PluginFormatterContribution(
    string Id,
    string DisplayName,
    string AppliesTo);

public interface IPluginFormatterRegistry
{
    ValueTask<Result<IReadOnlyList<PluginFormatterContribution>, DaemonError>> ListAsync(
        CancellationToken cancellationToken);
}

public sealed record PluginCommandContribution(
    string Id,
    string DisplayName,
    string? Description,
    string Target);

public interface IPluginCommandRegistry
{
    ValueTask<Result<IReadOnlyList<PluginCommandContribution>, DaemonError>> ListAsync(
        CancellationToken cancellationToken);
}

public sealed record PluginInstanceCreatePipelineContext(
    string InstanceType,
    IReadOnlyDictionary<string, string> Inputs);

public sealed record PluginInstanceCreatePipelineResult(
    IReadOnlyDictionary<string, string> Outputs,
    bool ContinuePipeline = true);

public interface IPluginInstanceCreatePipelineStage
{
    ValueTask<Result<PluginInstanceCreatePipelineResult, DaemonError>> ExecuteAsync(
        PluginInstanceCreatePipelineContext context,
        CancellationToken cancellationToken);
}

public sealed record PluginInstanceLifecycleEvent(
    string InstanceId,
    string Stage,
    IReadOnlyDictionary<string, string>? Metadata = null);

public interface IPluginInstanceLifecycleHook
{
    ValueTask<Result<Unit, DaemonError>> OnLifecycleAsync(
        PluginInstanceLifecycleEvent lifecycleEvent,
        CancellationToken cancellationToken);
}

public sealed record PluginProcessLauncherOverrideResult(
    bool Handled,
    PluginProcessLaunchPlan? Plan = null,
    string? Reason = null);

public interface IPluginProcessLauncherOverride
{
    ValueTask<Result<PluginProcessLauncherOverrideResult, DaemonError>> TryCreatePlanAsync(
        PluginProcessLaunchRequest request,
        CancellationToken cancellationToken);
}
