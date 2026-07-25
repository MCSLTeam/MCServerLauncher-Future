using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Plugins;
using MCServerLauncher.Daemon.Plugin.Sdk;
using Microsoft.Extensions.DependencyInjection;
using RustyOptions;

namespace MCServerLauncher.PluginFixtures.SdkGeneratedHealth;

[DaemonPluginModule]
public partial class SdkGeneratedHealthPlugin
{
    private SdkGeneratedHealthPluginFeatures? _features;

    public void ConfigureServices(IServiceCollection services, SdkGeneratedHealthPluginFeatures features)
    {
        _features = features;
        services.AddSingleton<SdkGeneratedResourceProbe>();
    }

    public async Task<Result<Unit, DaemonError>> StartAsync(CancellationToken cancellationToken)
    {
        var mode = Environment.GetEnvironmentVariable("MCSL_SDK_GENERATED_RESOURCE_MODE");
        if (string.IsNullOrWhiteSpace(mode))
            return PluginResult.Ok();

        _ = _features!.Services.GetRequiredService<SdkGeneratedResourceProbe>();
        var portText = Environment.GetEnvironmentVariable("MCSL_SDK_GENERATED_RESOURCE_PORT");
        if (!int.TryParse(portText, out var port))
            throw new InvalidOperationException("The generated resource probe port is missing.");
        var endpoint = _features.HttpEndpoints.ValidateAndRegister("127.0.0.1", port);
        if (endpoint.IsErr(out var endpointError))
            return Result.Err<Unit, DaemonError>(endpointError!);

        if (mode.Equals("throw", StringComparison.Ordinal))
            throw new InvalidOperationException("Generated resource probe start failure.");
        if (mode.Equals("timeout", StringComparison.Ordinal))
            await Task.Delay(Timeout.InfiniteTimeSpan, CancellationToken.None).ConfigureAwait(false);

        return PluginResult.Ok();
    }

    public Task<Result<Unit, DaemonError>> StopAsync(CancellationToken cancellationToken)
        => Task.FromResult(PluginResult.Ok());
}

internal sealed class SdkGeneratedResourceProbe : IAsyncDisposable
{
    // Captured once per probe: the driving test rewrites these process-global variables for every
    // theory case, so re-reading them per write let a probe that outlived its own case append into
    // the next case's file.
    private readonly string? _probePath =
        Environment.GetEnvironmentVariable("MCSL_SDK_GENERATED_RESOURCE_PROBE_PATH");

    private readonly string? _releasePath =
        Environment.GetEnvironmentVariable("MCSL_SDK_GENERATED_RESOURCE_DISPOSE_RELEASE_PATH");

    private int _disposed;

    public SdkGeneratedResourceProbe() => Record("created");

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        if (!string.IsNullOrWhiteSpace(_releasePath))
        {
            Record("disposing");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!File.Exists(_releasePath))
                await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token).ConfigureAwait(false);
        }

        Record("disposed");
    }

    private void Record(string value)
    {
        if (!string.IsNullOrWhiteSpace(_probePath))
            File.AppendAllText(_probePath, value + Environment.NewLine);
    }
}
