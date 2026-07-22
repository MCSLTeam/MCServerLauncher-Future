using System.Text.Json.Serialization;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Plugins;
using MCServerLauncher.Daemon.API.Protocol;
using Microsoft.Extensions.Logging;
using RustyOptions;

namespace MCServerLauncher.ExternalCompileFixture;

public sealed class ExternalCompilePlugin : IGeneratedDaemonPluginAdapter
{
    public static PluginAdapterMetadata Metadata { get; } = new(
        "external-compile",
        "1.0.0",
        "PluginEntry.dll",
        "MCServerLauncher.ExternalCompileFixture.ExternalCompilePlugin",
        "[2.0.0, 3.0.0)",
        ["event.publish", "instance.query", "rpc.register"],
        "72359393f00a7d0bfbed2f1b684a8583146b0212c1ac29bc8477f37af1815d61");

    private IPluginContext? _context;

    public Result<Unit, DaemonError> Configure(IPluginContext context)
    {
        _context = context;
        var rpc = PluginProtocol.CreateRpc(
            "plugin.external-compile.rpc.ping",
            "plugin.external-compile.rpc",
            FixtureJsonContext.Default.EmptyRequest,
            FixtureJsonContext.Default.UnitResult,
            documentation: new RpcDocumentation(
                "fixture",
                "Ping",
                "Returns a unit result.",
                "fixture.empty-request",
                "fixture.unit-result"));
        var rpcResult = context.Rpc.Register(rpc, static (_, _) =>
            Task.FromResult(PluginResult.Ok<UnitResult>(new UnitResult())));
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
