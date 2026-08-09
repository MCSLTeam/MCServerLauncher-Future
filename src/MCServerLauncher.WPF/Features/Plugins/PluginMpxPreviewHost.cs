using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace MCServerLauncher.WPF.Features.Plugins;

public sealed record PluginMpxPreviewRequest(
    byte[] PackageBytes,
    IReadOnlyDictionary<string, object?> InitialState);

public sealed record PluginMpxPreviewResult(
    bool Ok,
    FrameworkElement? Root,
    PluginScriptExecutionResult? ScriptResult,
    IReadOnlyList<PluginMpxDiagnostic> PackageDiagnostics,
    IReadOnlyList<PluginUiEvent> Events,
    IReadOnlyList<PluginUiHostLogEntry> Logs)
{
    public static PluginMpxPreviewResult PackageRejected(IReadOnlyList<PluginMpxDiagnostic> diagnostics) =>
        new(false, null, null, diagnostics, [], []);

    public static PluginMpxPreviewResult Rendered(
        FrameworkElement root,
        PluginScriptExecutionResult scriptResult,
        IReadOnlyList<PluginUiEvent> events,
        IReadOnlyList<PluginUiHostLogEntry> logs) =>
        new(true, root, scriptResult, [], events, logs);
}

public sealed class PluginMpxPreviewHost(
    PluginUiRenderer? renderer = null,
    Func<IPluginScriptRuntime>? runtimeFactory = null,
    PluginScriptRuntimeOptions? scriptOptions = null,
    IPluginUiDaemonQueryBridge? daemonQueryBridge = null)
{
    private readonly PluginUiRenderer _renderer = renderer ?? new PluginUiRenderer();
    private readonly PluginScriptRuntimeOptions _scriptOptions = scriptOptions ?? new PluginScriptRuntimeOptions();
    private readonly Func<IPluginScriptRuntime> _runtimeFactory = runtimeFactory ?? (() => new GuardedPluginScriptRuntime(scriptOptions));
    private readonly IPluginUiDaemonQueryBridge _daemonQueryBridge = daemonQueryBridge ?? PluginUiDaemonQueryBridge.Empty;

    public async Task<PluginMpxPreviewResult> RenderAsync(
        PluginMpxPreviewRequest request,
        Dispatcher dispatcher,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(dispatcher);

        var validation = PluginMpxPackageValidator.Validate(request.PackageBytes);
        if (!validation.Ok || validation.Package is null)
            return PluginMpxPreviewResult.PackageRejected(validation.Diagnostics);

        if (validation.Package.UiSchema is null)
        {
            return PluginMpxPreviewResult.PackageRejected(
            [
                new PluginMpxDiagnostic(
                    "client_ui_missing",
                    "$.targets.client.ui",
                    "Preview rendering requires a client UI target.")
            ]);
        }

        Dictionary<string, object?> state = new(request.InitialState, StringComparer.Ordinal);
        List<PluginUiEvent> events = [];
        List<PluginUiHostLogEntry> logs = [];
        var hostCapabilities = new HashSet<string>(validation.Package.HostCapabilities, StringComparer.Ordinal);
        var hostEvents = new HashSet<string>(validation.Package.EventPermissions, StringComparer.Ordinal);
        var host = new PluginUiStateHost(
            events.Add,
            patch => ApplyStatePatch(state, patch),
            _daemonQueryBridge.QueryInstanceCatalogAsync,
            logs.Add,
            hostCapabilities,
            hostEvents: hostEvents);

        var scriptResult = PluginScriptExecutionResult.Ok();
        if (validation.Package.JavaScriptModule is { Length: > 0 } script)
        {
            await using var runtime = _runtimeFactory();
            scriptResult = await ExecuteScriptWithTimeoutAsync(
                runtime,
                new PluginScriptExecutionRequest(
                    validation.Package.PluginId,
                    script,
                    state,
                    host),
                _scriptOptions,
                cancellationToken).ConfigureAwait(false);
        }

        var root = await _renderer.RenderAsync(
            validation.Package.UiSchema,
            state,
            host,
            dispatcher,
            cancellationToken).ConfigureAwait(false);

        return PluginMpxPreviewResult.Rendered(
            root,
            scriptResult,
            new ReadOnlyCollection<PluginUiEvent>(events),
            new ReadOnlyCollection<PluginUiHostLogEntry>(logs));
    }

    private static async Task<PluginScriptExecutionResult> ExecuteScriptWithTimeoutAsync(
        IPluginScriptRuntime runtime,
        PluginScriptExecutionRequest request,
        PluginScriptRuntimeOptions options,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var executeTask = runtime.ExecuteAsync(request, timeout.Token);
        var timeoutTask = Task.Delay(options.EffectiveExecutionTimeout, CancellationToken.None);
        var completed = await Task.WhenAny(executeTask, timeoutTask).ConfigureAwait(false);
        if (completed == executeTask)
        {
            try
            {
                return await executeTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                return PluginScriptExecutionResult.Fail("script_cancelled", "Plugin script execution was cancelled before completion.");
            }
            catch (Exception ex)
            {
                return PluginScriptExecutionResult.Fail("script_exception", ex.Message);
            }
        }

        timeout.Cancel();
        ObserveFaults(executeTask);
        return PluginScriptExecutionResult.Fail("script_timeout", $"Plugin script execution exceeded {options.EffectiveExecutionTimeout.TotalMilliseconds:0} ms.");
    }

    private static void ObserveFaults(Task task)
    {
        _ = task.ContinueWith(
            completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }

    private static void ApplyStatePatch(
        IDictionary<string, object?> state,
        IReadOnlyDictionary<string, object?> patch)
    {
        foreach (var item in patch)
            state[item.Key] = item.Value;
    }
}
