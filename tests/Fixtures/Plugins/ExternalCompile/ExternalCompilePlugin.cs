using System.Text.Json.Serialization;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Plugins;
using MCServerLauncher.Daemon.API.Protocol;
using Microsoft.Extensions.Logging;
using RustyOptions;

[assembly: GeneratedDaemonPluginMetadata(
    "external-compile",
    "1.0.0",
    "PluginEntry.dll",
    "MCServerLauncher.ExternalCompileFixture.ExternalCompilePlugin",
    "[1.0.0, 2.0.0)",
    "event.publish\ninstance.query\nrpc.register",
    "3277e1b6d65f1dfc36328070353f04757259bca2d283672861e39fbf1cd8c0ce")]

namespace MCServerLauncher.ExternalCompileFixture;

public sealed class ExternalCompilePlugin : IGeneratedDaemonPluginAdapter
{
    private IPluginContext? _context;

    public Result<Unit, DaemonError> Configure(IPluginContext context)
    {
        _context = context;
        var rpcResult = context.Rpc.Register(
            "ping",
            FixtureJsonContext.Default.EmptyRequest,
            FixtureJsonContext.Default.UnitResult,
            documentation: new RpcDocumentation(
                "fixture",
                "Ping",
                "Returns a unit result.",
                "fixture.empty-request",
                "fixture.unit-result"),
            handler: static (_, _) => Task.FromResult(PluginResult.Ok<UnitResult>(new UnitResult())));
        _ = rpcResult;

        Result<UnitResult, DaemonError> errorResult = context.Errors.Fail<UnitResult>(
            "fixture.failure",
            "Fixture failure.");
        _ = errorResult;

        var eventDescriptor = PluginProtocol.CreateEvent<UnitResult, EmptyRequest>(
            "plugin.external-compile.event.changed",
            "plugin.external-compile.event",
            FixtureJsonContext.Default.UnitResult,
            null,
            new EventDocumentation(
                "fixture",
                "Changed",
                "Publishes a unit event.",
                "fixture.unit-result",
                null));
        _ = context.Events.Register(eventDescriptor);
        return PluginResult.Ok();
    }

    public Task<Result<Unit, DaemonError>> StartAsync(CancellationToken cancellationToken) =>
        Task.FromResult(PluginResult.Ok());

    public Task<Result<Unit, DaemonError>> StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _context?.Logger.LogInformation("fixture.external_compile.stop");
        return Task.FromResult(PluginResult.Ok());
    }
}

[JsonSerializable(typeof(EmptyRequest))]
[JsonSerializable(typeof(UnitResult))]
internal partial class FixtureJsonContext : JsonSerializerContext;
