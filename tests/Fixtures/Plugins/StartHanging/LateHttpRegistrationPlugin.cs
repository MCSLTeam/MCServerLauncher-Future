using System.Globalization;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Plugins;
using RustyOptions;

[assembly: GeneratedDaemonPluginMetadata(
    "fixture.late-http-cleanup",
    "1.0.0",
    "PluginEntry.dll",
    "MCServerLauncher.PluginFixtures.StartHanging.LateHttpRegistrationPlugin",
    "[2.0.0, 3.0.0)",
    "network.http.listen",
    "12dc8111bc8ec1f51d01a90e7511899fb7ab9b1e9d0da5bc2578bee88ebafc46")]

namespace MCServerLauncher.PluginFixtures.StartHanging;

public sealed class LateHttpRegistrationPlugin : IGeneratedDaemonPluginAdapter, IAsyncDisposable
{
    private IPluginContext? _context;
    private FileStream? _constructionResource;

    public LateHttpRegistrationPlugin()
    {
        var metadataProbePath = Environment.GetEnvironmentVariable("MCSL_PLUGIN_METADATA_PROBE_PATH");
        if (!string.IsNullOrWhiteSpace(metadataProbePath))
            File.WriteAllText(metadataProbePath, GetType().FullName ?? GetType().Name);

        var resourcePath = Environment.GetEnvironmentVariable("MCSL_LATE_HTTP_RESOURCE_PATH");
        if (!string.IsNullOrWhiteSpace(resourcePath))
        {
            _constructionResource = new FileStream(
                resourcePath,
                FileMode.Create,
                FileAccess.ReadWrite,
                FileShare.None);
        }
    }

    public Result<Unit, DaemonError> Configure(IPluginContext context)
    {
        _context = context;
        var ownedPort = int.Parse(
            GetRequiredEnvironmentVariable("MCSL_LATE_HTTP_OWNED_PORT"),
            NumberStyles.None,
            CultureInfo.InvariantCulture);
        var registration = context.HttpEndpoints.ValidateAndRegister("127.0.0.1", ownedPort);
        if (registration.IsErr(out var registrationError))
            return Result.Err<Unit, DaemonError>(registrationError!);

        if (!ModeIs("configure-failure"))
            return PluginResult.Ok();

        ScheduleLateRegistration();
        return Result.Err<Unit, DaemonError>(context.Errors.Create(
            "fixture_configure_failure",
            "Fixture fails configuration after scheduling a late endpoint registration."));
    }

    public Task<Result<Unit, DaemonError>> StartAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        if (!ModeIs("start-timeout"))
            return Task.FromResult(PluginResult.Ok());

        ScheduleLateRegistration();
        return new TaskCompletionSource<Result<Unit, DaemonError>>(
            TaskCreationOptions.RunContinuationsAsynchronously).Task;
    }

    public Task<Result<Unit, DaemonError>> StopAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        if (ModeIs("shutdown") || ModeIs("dispose-fault"))
            ScheduleLateRegistration();
        return Task.FromResult(PluginResult.Ok());
    }

    public async ValueTask DisposeAsync()
    {
        if (_context is not null)
        {
            var ownedPort = int.Parse(
                GetRequiredEnvironmentVariable("MCSL_LATE_HTTP_OWNED_PORT"),
                NumberStyles.None,
                CultureInfo.InvariantCulture);
            _context.HttpEndpoints.Release("127.0.0.1", ownedPort);
        }

        var enteredPath = Environment.GetEnvironmentVariable("MCSL_LATE_HTTP_DISPOSE_ENTERED_PATH");
        if (!string.IsNullOrWhiteSpace(enteredPath))
            File.WriteAllText(enteredPath, string.Empty);

        if (ModeIs("dispose-fault"))
            throw new InvalidOperationException("Fixture disposal failure after endpoint admission closed.");

        var releasePath = Environment.GetEnvironmentVariable("MCSL_LATE_HTTP_DISPOSE_RELEASE_PATH");
        if (!string.IsNullOrWhiteSpace(releasePath))
        {
            while (!File.Exists(releasePath))
                await Task.Delay(TimeSpan.FromMilliseconds(10)).ConfigureAwait(false);
        }

        var resource = Interlocked.Exchange(ref _constructionResource, null);
        if (resource is null)
            return;

        await resource.DisposeAsync().ConfigureAwait(false);
        File.WriteAllText(resource.Name + ".disposed", string.Empty);
    }

    private void ScheduleLateRegistration()
    {
        var endpoints = _context!.HttpEndpoints;
        var signalPath = GetRequiredEnvironmentVariable("MCSL_LATE_HTTP_SIGNAL_PATH");
        var resultPath = GetRequiredEnvironmentVariable("MCSL_LATE_HTTP_RESULT_PATH");
        var port = int.Parse(
            GetRequiredEnvironmentVariable("MCSL_LATE_HTTP_PORT"),
            NumberStyles.None,
            CultureInfo.InvariantCulture);

        _ = Task.Run(async () =>
        {
            try
            {
                while (!File.Exists(signalPath))
                    await Task.Delay(TimeSpan.FromMilliseconds(10)).ConfigureAwait(false);

                var registration = endpoints.ValidateAndRegister("127.0.0.1", port);
                var outcome = registration.IsErr(out var error) ? error!.Code : "registered";
                await File.WriteAllTextAsync(resultPath, outcome).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                await File.WriteAllTextAsync(resultPath, $"exception:{exception.GetType().Name}")
                    .ConfigureAwait(false);
            }
        });
    }

    private static bool ModeIs(string expected) => string.Equals(
        Environment.GetEnvironmentVariable("MCSL_LATE_HTTP_MODE"),
        expected,
        StringComparison.Ordinal);

    private static string GetRequiredEnvironmentVariable(string name) =>
        Environment.GetEnvironmentVariable(name) ??
        throw new InvalidOperationException($"Required fixture environment variable '{name}' is missing.");
}
