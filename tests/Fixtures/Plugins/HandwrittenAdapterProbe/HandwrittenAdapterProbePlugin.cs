using System.Runtime.CompilerServices;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Plugins;
using Microsoft.Extensions.DependencyInjection;
using RustyOptions;

[assembly: GeneratedDaemonPluginMetadata(
    "fixture.handwritten-adapter",
    "1.0.0",
    "PluginEntry.dll",
    "MCServerLauncher.PluginFixtures.HandwrittenAdapterProbe.HandwrittenAdapterProbePlugin",
    "[2.0.0, 3.0.0)",
    "instance.query",
    "e8b6476b070233e31cd3f41e1d5079fd492c626aa5dd9fcba9cd72124aedb108")]
[assembly: GeneratedDaemonPluginMetadata(
    "fixture.handwritten-adapter-generated",
    "1.0.0",
    "PluginEntry.dll",
    "MCServerLauncher.PluginFixtures.HandwrittenAdapterProbe.MetadataRejectedProbePlugin",
    "[2.0.0, 3.0.0)",
    "instance.query",
    "0000000000000000000000000000000000000000000000000000000000000000")]

namespace MCServerLauncher.PluginFixtures.HandwrittenAdapterProbe;

internal static class AssemblyLoadSentinel
{
#pragma warning disable CA2255 // The fixture intentionally proves metadata rejection before assembly IL runs.
    [ModuleInitializer]
    internal static void Initialize()
    {
        var path = Environment.GetEnvironmentVariable("MCSL_HANDWRITTEN_ADAPTER_MODULE_SENTINEL");
        if (!string.IsNullOrWhiteSpace(path))
            File.WriteAllText(path, "module-initializer");
    }
#pragma warning restore CA2255
}

public sealed class HandwrittenAdapterProbePlugin : IGeneratedDaemonPluginAdapter, IDisposable
{
    private IPluginContext? _context;
    private ServiceProvider? _services;

    public Result<Unit, DaemonError> Configure(IPluginContext context)
    {
        _context = context;

        var services = new ServiceCollection();
        services.AddSingleton(context.InstanceQueries);
        _services = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });

        return PluginResult.Ok();
    }

    public async Task<Result<Unit, DaemonError>> StartAsync(CancellationToken cancellationToken)
    {
        var context = _context ?? throw new InvalidOperationException("Configure must run first.");
        var services = _services ?? throw new InvalidOperationException("Private services must be built first.");
        var privateQueries = services.GetRequiredService<IInstanceQueryApplication>();

        var contextResult = await context.InstanceQueries
            .ListInstanceReportsAsync(cancellationToken)
            .ConfigureAwait(false);
        var privateResult = await privateQueries
            .ListInstanceReportsAsync(cancellationToken)
            .ConfigureAwait(false);

        var undeclaredContextDenied = false;
        try
        {
            _ = context.OperationControl;
        }
        catch (InvalidOperationException)
        {
            undeclaredContextDenied = true;
        }

        var probePath = Environment.GetEnvironmentVariable("MCSL_HANDWRITTEN_ADAPTER_PROBE_PATH");
        if (!string.IsNullOrWhiteSpace(probePath))
        {
            File.WriteAllLines(probePath,
            [
                $"contextQueries={context.InstanceQueries.GetType().FullName}",
                $"privateQueries={privateQueries.GetType().FullName}",
                $"contextRawInstance={context.InstanceQueries is IInstanceApplication}",
                $"privateRawInstance={services.GetService<IInstanceApplication>() is not null}",
                $"privateRawContext={services.GetService<IPluginContext>() is not null}",
                $"undeclaredContextDenied={undeclaredContextDenied}",
                $"privateUndeclared={services.GetService<IOperationControlApplication>() is not null}",
                $"contextResult={GetErrorCode(contextResult)}",
                $"privateResult={GetErrorCode(privateResult)}",
            ]);
        }

        return PluginResult.Ok();
    }

    public Task<Result<Unit, DaemonError>> StopAsync(CancellationToken cancellationToken) =>
        Task.FromResult(PluginResult.Ok());

    public void Dispose() => _services?.Dispose();

    private static string GetErrorCode<T>(Result<T, DaemonError> result)
        where T : notnull =>
        result.IsErr(out var error) ? error!.Code : "ok";
}

public sealed class MetadataRejectedProbePlugin : IGeneratedDaemonPluginAdapter
{
    public Result<Unit, DaemonError> Configure(IPluginContext context) => PluginResult.Ok();

    public Task<Result<Unit, DaemonError>> StartAsync(CancellationToken cancellationToken) =>
        Task.FromResult(PluginResult.Ok());

    public Task<Result<Unit, DaemonError>> StopAsync(CancellationToken cancellationToken) =>
        Task.FromResult(PluginResult.Ok());
}
