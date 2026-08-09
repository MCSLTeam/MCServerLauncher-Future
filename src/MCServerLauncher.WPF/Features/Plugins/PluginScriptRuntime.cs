using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MCServerLauncher.WPF.Features.Plugins;

public enum PluginScriptRuntimeMode
{
    Disabled
}

public sealed record PluginScriptRuntimeOptions(
    int MaxScriptBytes = 512 * 1024,
    int MaxStateBytes = 256 * 1024,
    TimeSpan ExecutionTimeout = default,
    TimeSpan DisposeTimeout = default,
    PluginScriptRuntimeMode Mode = PluginScriptRuntimeMode.Disabled)
{
    public TimeSpan EffectiveExecutionTimeout => ExecutionTimeout == default ? TimeSpan.FromMilliseconds(250) : ExecutionTimeout;
    public TimeSpan EffectiveDisposeTimeout => DisposeTimeout == default ? TimeSpan.FromMilliseconds(100) : DisposeTimeout;
}

public sealed record PluginScriptExecutionRequest(
    string PluginId,
    string JavaScriptModule,
    IReadOnlyDictionary<string, object?> InitialState,
    PluginUiStateHost Host);

public sealed record PluginScriptExecutionResult(bool Success, string Code, string Message)
{
    public static PluginScriptExecutionResult Ok() => new(true, "ok", string.Empty);

    public static PluginScriptExecutionResult Fail(string code, string message) => new(false, code, message);
}

public interface IPluginScriptRuntime : IAsyncDisposable
{
    Task<PluginScriptExecutionResult> ExecuteAsync(
        PluginScriptExecutionRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class GuardedPluginScriptRuntime(PluginScriptRuntimeOptions? options = null) : IPluginScriptRuntime
{
    private readonly PluginScriptRuntimeOptions _options = options ?? new PluginScriptRuntimeOptions();
    private bool _disposed;

    public Task<PluginScriptExecutionResult> ExecuteAsync(
        PluginScriptExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_disposed)
            return Task.FromResult(PluginScriptExecutionResult.Fail("runtime_disposed", "The plugin script runtime has already been disposed."));
        if (string.IsNullOrWhiteSpace(request.PluginId))
            return Task.FromResult(PluginScriptExecutionResult.Fail("plugin_id_invalid", "Plugin id is required."));
        if (request.JavaScriptModule.Length > _options.MaxScriptBytes)
            return Task.FromResult(PluginScriptExecutionResult.Fail("script_too_large", "Plugin JavaScript exceeds the configured runtime limit."));
        if (!TryGetInitialStateSize(request.InitialState, out var stateBytes, out var stateFailure))
            return Task.FromResult(stateFailure);
        if (stateBytes > _options.MaxStateBytes)
            return Task.FromResult(PluginScriptExecutionResult.Fail("state_too_large", "Plugin initial state exceeds the configured runtime limit."));

        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(PluginScriptExecutionResult.Fail(
            "script_runtime_unavailable",
            "JavaScript execution is intentionally disabled. Client extensions should use declarative UI schema and Extension Protocol command bindings; daemon business logic runs through C# plugins."));
    }

    private static bool TryGetInitialStateSize(
        IReadOnlyDictionary<string, object?> state,
        out int bytes,
        out PluginScriptExecutionResult failure)
    {
        try
        {
            bytes = JsonSerializer.SerializeToUtf8Bytes(state).Length;
            failure = PluginScriptExecutionResult.Ok();
            return true;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            bytes = 0;
            failure = PluginScriptExecutionResult.Fail("state_invalid", "Plugin initial state must be JSON-serializable.");
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        using var timeout = new CancellationTokenSource(_options.EffectiveDisposeTimeout);
        await Task.Yield();
        timeout.Token.ThrowIfCancellationRequested();
        _disposed = true;
    }
}
