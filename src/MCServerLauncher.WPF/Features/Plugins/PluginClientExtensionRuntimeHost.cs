using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace MCServerLauncher.WPF.Features.Plugins;

public sealed record PluginClientExtensionRuntimeDiagnostic(string Code, string Path, string Message);

public sealed record PluginClientExtensionRenderRequest(
    PluginClientExtensionCacheEntry Entry,
    IReadOnlyDictionary<string, object?> InitialState,
    Func<PluginUiEvent, Task>? EventHandler = null);

public sealed record PluginClientExtensionRenderResult(
    bool Ok,
    FrameworkElement? Root,
    PluginScriptExecutionResult? ScriptResult,
    IReadOnlyList<PluginClientExtensionRuntimeDiagnostic> Diagnostics,
    IReadOnlyList<PluginUiEvent> Events,
    IReadOnlyList<PluginUiHostLogEntry> Logs)
{
    public static PluginClientExtensionRenderResult Failure(IReadOnlyList<PluginClientExtensionRuntimeDiagnostic> diagnostics) =>
        new(false, null, null, diagnostics, [], []);

    public static PluginClientExtensionRenderResult Rendered(
        FrameworkElement root,
        PluginScriptExecutionResult scriptResult,
        IReadOnlyList<PluginUiEvent> events,
        IReadOnlyList<PluginUiHostLogEntry> logs) =>
        new(true, root, scriptResult, [], events, logs);
}

public sealed record PluginClientExtensionResourcePathResult(
    bool Ok,
    string? Path,
    string? Code,
    string? Message)
{
    public static PluginClientExtensionResourcePathResult Success(string path) =>
        new(true, path, null, null);

    public static PluginClientExtensionResourcePathResult Failure(string code, string message) =>
        new(false, null, code, message);
}

public sealed record PluginClientExtensionEventDispatchResult(
    bool Dispatched,
    IReadOnlyList<PluginExtensionProtocolDiagnostic> Diagnostics)
{
    public static PluginClientExtensionEventDispatchResult Success() => new(true, []);

    public static PluginClientExtensionEventDispatchResult Failure(IReadOnlyList<PluginExtensionProtocolDiagnostic> diagnostics) => new(false, diagnostics);
}

public sealed class PluginClientExtensionRuntimeHost(
    PluginUiRenderer? renderer = null,
    Func<IPluginScriptRuntime>? runtimeFactory = null,
    PluginScriptRuntimeOptions? scriptOptions = null,
    IPluginUiDaemonQueryBridge? daemonQueryBridge = null,
    IPluginExtensionCommandDispatcher? commandDispatcher = null)
{
    private readonly PluginUiRenderer _renderer = renderer ?? new PluginUiRenderer();
    private readonly PluginScriptRuntimeOptions _scriptOptions = scriptOptions ?? new PluginScriptRuntimeOptions();
    private readonly Func<IPluginScriptRuntime> _runtimeFactory = runtimeFactory ?? (() => new GuardedPluginScriptRuntime(scriptOptions));
    private readonly IPluginUiDaemonQueryBridge _daemonQueryBridge = daemonQueryBridge ?? PluginUiDaemonQueryBridge.Empty;
    private readonly IPluginExtensionCommandDispatcher? _commandDispatcher = commandDispatcher;

    public async Task<PluginClientExtensionRenderResult> RenderAsync(
        PluginClientExtensionRenderRequest request,
        Dispatcher dispatcher,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Entry);
        ArgumentNullException.ThrowIfNull(dispatcher);

        var entry = request.Entry;
        if (entry.UiSchema is null)
        {
            return PluginClientExtensionRenderResult.Failure(
            [
                Diagnostic("client_ui_missing", "$.targets.client.ui", "Installed extension has no renderable client UI schema.")
            ]);
        }

        Dictionary<string, object?> state = new(request.InitialState, StringComparer.Ordinal);
        List<PluginUiEvent> events = [];
        List<PluginUiHostLogEntry> logs = [];
        var hostCapabilities = new HashSet<string>(entry.HostCapabilities, StringComparer.Ordinal);
        var hostEvents = new HashSet<string>(entry.EventPermissions, StringComparer.Ordinal);
        var host = new PluginUiStateHost(
            uiEvent => EmitEvent(uiEvent, events, request.EventHandler, logs),
            patch => ApplyStatePatch(state, patch),
            _daemonQueryBridge.QueryInstanceCatalogAsync,
            logs.Add,
            hostCapabilities,
            hostEvents: hostEvents);

        var scriptResult = PluginScriptExecutionResult.Ok();
        if (entry.DeploymentPlan.Client?.Script is { } script)
        {
            var resolved = ResolveCachedPath(entry, script.Path);
            if (!resolved.Ok || resolved.Path is null)
            {
                return PluginClientExtensionRenderResult.Failure(
                [
                    Diagnostic(resolved.Code ?? "client_script_missing", "$.entry.script.path", resolved.Message ?? "Installed extension script is not cached.")
                ]);
            }

            var module = await File.ReadAllTextAsync(resolved.Path, cancellationToken).ConfigureAwait(false);
            await using var runtime = _runtimeFactory();
            scriptResult = await ExecuteScriptWithTimeoutAsync(
                runtime,
                new PluginScriptExecutionRequest(entry.Id, module, state, host),
                _scriptOptions,
                cancellationToken).ConfigureAwait(false);
        }

        var root = await _renderer.RenderAsync(
            entry.UiSchema,
            state,
            host,
            dispatcher,
            cancellationToken).ConfigureAwait(false);

        return PluginClientExtensionRenderResult.Rendered(
            root,
            scriptResult,
            new ReadOnlyCollection<PluginUiEvent>(events),
            new ReadOnlyCollection<PluginUiHostLogEntry>(logs));
    }

    public static PluginExtensionStateSnapshot CreateState(
        PluginClientExtensionCacheEntry entry,
        IReadOnlyDictionary<string, object?>? initialState = null,
        long revision = 0)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new PluginExtensionStateSnapshot(
            entry.Id,
            revision,
            new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>(initialState ?? new Dictionary<string, object?>(), StringComparer.Ordinal)));
    }

    public static PluginExtensionStatePatchResult ApplyStateEnvelope(
        PluginClientExtensionCacheEntry entry,
        PluginExtensionStateSnapshot snapshot,
        PluginExtensionProtocolParseResult parsed)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(parsed);

        if (!StringComparer.Ordinal.Equals(snapshot.Plugin, entry.Id))
            return new PluginExtensionStatePatchResult(false, snapshot);

        return PluginExtensionProtocol.ApplyStateEnvelope(snapshot, parsed);
    }

    public static async Task<PluginClientExtensionEventDispatchResult> DispatchEventAsync(
        PluginClientExtensionCacheEntry entry,
        PluginExtensionProtocolParseResult parsed,
        Func<PluginExtensionEventEnvelope, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(parsed);
        ArgumentNullException.ThrowIfNull(handler);

        if (parsed.Event is null)
        {
            return PluginClientExtensionEventDispatchResult.Failure(
            [
                new PluginExtensionProtocolDiagnostic("event_missing", "$", "Envelope is not an event.")
            ]);
        }
        if (!StringComparer.Ordinal.Equals(parsed.Event.Plugin, entry.Id))
        {
            return PluginClientExtensionEventDispatchResult.Failure(
            [
                new PluginExtensionProtocolDiagnostic("event_plugin_mismatch", "$.plugin", "Event plugin does not match the installed extension.")
            ]);
        }
        if (!entry.EventPermissions.Contains(parsed.Event.Name, StringComparer.Ordinal))
        {
            return PluginClientExtensionEventDispatchResult.Failure(
            [
                new PluginExtensionProtocolDiagnostic("event_undeclared", "$.name", $"Event '{parsed.Event.Name}' is not declared by installed extension '{entry.Id}'.")
            ]);
        }

        try
        {
            await handler(parsed.Event).ConfigureAwait(false);
            return PluginClientExtensionEventDispatchResult.Success();
        }
        catch (Exception ex)
        {
            return PluginClientExtensionEventDispatchResult.Failure(
            [
                new PluginExtensionProtocolDiagnostic("event_handler_failed", "$", ex.Message)
            ]);
        }
    }

    public Task<PluginExtensionProtocolParseResult> DispatchCommandAsync(
        PluginClientExtensionCacheEntry entry,
        PluginUiCommandEvent command,
        string? requestId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        if (!entry.Commands.Any(declared => StringComparer.Ordinal.Equals(declared.Id, command.Command) && StringComparer.Ordinal.Equals(declared.Target, "daemon")))
        {
            return Task.FromResult(PluginExtensionProtocolParseResult.Failure(
            [
                new PluginExtensionProtocolDiagnostic(
                    "command_undeclared",
                    "$.command",
                    $"Command '{command.Command}' is not declared by installed extension '{entry.Id}'.")
            ]));
        }

        if (_commandDispatcher is null)
        {
            return Task.FromResult(PluginExtensionProtocolParseResult.Failure(
            [
                new PluginExtensionProtocolDiagnostic(
                    "dispatch_unavailable",
                    "$",
                    "No Extension Protocol command dispatcher is configured for this client extension host.")
            ]));
        }

        return _commandDispatcher.DispatchAsync(entry.Id, command, requestId, cancellationToken);
    }

    public static PluginClientExtensionResourcePathResult ResolveCachedPath(
        PluginClientExtensionCacheEntry entry,
        string packagePath)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);

        if (!DeclaredClientPaths(entry).Contains(packagePath, StringComparer.Ordinal))
        {
            return PluginClientExtensionResourcePathResult.Failure(
                "resource_undeclared",
                $"Resource '{packagePath}' is not declared by extension '{entry.Id}'.");
        }

        if (!entry.CachedFiles.TryGetValue(packagePath, out var cachedPath) || string.IsNullOrWhiteSpace(cachedPath))
        {
            return PluginClientExtensionResourcePathResult.Failure(
                "resource_unavailable",
                $"Resource '{packagePath}' is declared but has no cached file path.");
        }

        if (!File.Exists(cachedPath))
        {
            return PluginClientExtensionResourcePathResult.Failure(
                "resource_missing",
                $"Cached resource '{packagePath}' is missing from disk.");
        }

        return PluginClientExtensionResourcePathResult.Success(cachedPath);
    }

    private static IEnumerable<string> DeclaredClientPaths(PluginClientExtensionCacheEntry entry)
    {
        var client = entry.DeploymentPlan.Client;
        if (client?.Ui is not null)
            yield return client.Ui.Path;
        if (client?.Theme is not null)
            yield return client.Theme.Path;
        if (client?.Script is not null)
            yield return client.Script.Path;
        if (client is not null)
        {
            foreach (var resource in client.Resources)
                yield return resource.Path;
        }
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

    private static void EmitEvent(
        PluginUiEvent uiEvent,
        List<PluginUiEvent> events,
        Func<PluginUiEvent, Task>? eventHandler,
        List<PluginUiHostLogEntry> logs)
    {
        events.Add(uiEvent);
        if (eventHandler is null)
            return;

        var task = eventHandler(uiEvent);
        if (task.IsCompletedSuccessfully)
            return;

        _ = task.ContinueWith(
            completed =>
            {
                if (completed.Exception is not null)
                    logs.Add(new PluginUiHostLogEntry(PluginUiHostLogLevel.Error, completed.Exception.GetBaseException().Message));
            },
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

    private static PluginClientExtensionRuntimeDiagnostic Diagnostic(string code, string path, string message) =>
        new(code, path, message);
}
