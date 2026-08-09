using System;
using System.Threading;
using System.Threading.Tasks;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon.API.Errors;
using RustyOptions;
using TypedDaemonClient = MCServerLauncher.DaemonClient.DaemonClient;

namespace MCServerLauncher.WPF.Features.Plugins;

public interface IPluginExtensionCommandDispatcher
{
    Task<PluginExtensionProtocolParseResult> DispatchAsync(
        string pluginId,
        PluginUiCommandEvent command,
        string? requestId = null,
        CancellationToken cancellationToken = default);
}

public sealed class PluginExtensionCommandDispatcher(
    Func<ExtensionProtocolDispatchRequest, CancellationToken, Task<Result<ExtensionProtocolDispatchResult, DaemonError>>> dispatch)
    : IPluginExtensionCommandDispatcher
{
    public static IPluginExtensionCommandDispatcher FromDaemonClient(TypedDaemonClient daemon)
    {
        ArgumentNullException.ThrowIfNull(daemon);
        return new PluginExtensionCommandDispatcher(daemon.DispatchExtensionProtocolAsync);
    }

    public async Task<PluginExtensionProtocolParseResult> DispatchAsync(
        string pluginId,
        PluginUiCommandEvent command,
        string? requestId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var envelope = PluginExtensionProtocol.CreateRequestEnvelope(
            string.IsNullOrWhiteSpace(requestId) ? Guid.NewGuid().ToString("N") : requestId,
            pluginId,
            command.Command,
            command.Params);

        var result = await dispatch(new ExtensionProtocolDispatchRequest(envelope), cancellationToken).ConfigureAwait(false);
        if (result.IsErr(out var error))
        {
            return PluginExtensionProtocolParseResult.Failure([
                new PluginExtensionProtocolDiagnostic(
                    "dispatch_failed",
                    "$",
                    error is null ? "Extension command dispatch failed." : $"{error.Code}: {error.Message}")
            ]);
        }

        return PluginExtensionProtocol.Parse(result.Unwrap().Envelope.GetRawText());
    }
}
