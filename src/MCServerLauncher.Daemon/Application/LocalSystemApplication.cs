using System.Collections.Immutable;
using MCServerLauncher.Common.Contracts.System;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Plugins;
using MCServerLauncher.Daemon.Plugins;
using MCServerLauncher.Daemon.Utils.LazyCell;
using RustyOptions;

namespace MCServerLauncher.Daemon.ApplicationCore;

internal sealed class LocalSystemApplication(
    IAsyncTimedLazyCell<SystemInfo> systemInfoCell,
    IAsyncTimedLazyCell<JavaRuntimeList> javaRuntimeCell,
    PluginExtensionPointRuntimeRegistry? extensionPointRuntimeRegistry = null) : ISystemApplication
{
    public async Task<Result<SystemInfo, DaemonError>> GetSystemInfoAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var systemInfo = await systemInfoCell.Value.AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
            return Result.Ok<SystemInfo, DaemonError>(systemInfo);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Result.Err<SystemInfo, DaemonError>(
                new InternalDaemonError("system.info_unavailable", "System information is unavailable."));
        }
    }

    public async Task<Result<JavaRuntimeList, DaemonError>> ListJavaRuntimesAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var runtimes = await javaRuntimeCell.Value.AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
            var contributed = await ListContributedJavaRuntimesAsync(cancellationToken).ConfigureAwait(false);
            if (contributed.IsErr(out var error))
                return Result.Err<JavaRuntimeList, DaemonError>(error!);

            var contributedRuntimes = contributed.Unwrap();
            if (contributedRuntimes.Length == 0)
                return Result.Ok<JavaRuntimeList, DaemonError>(runtimes);

            var paths = new HashSet<string>(runtimes.Items.Select(static runtime => runtime.Path), StringComparer.Ordinal);
            var merged = ImmutableArray.CreateBuilder<JavaRuntime>(runtimes.Items.Length + contributedRuntimes.Length);
            merged.AddRange(runtimes.Items);
            foreach (var runtime in contributedRuntimes)
            {
                if (paths.Add(runtime.Path))
                    merged.Add(runtime);
            }

            return Result.Ok<JavaRuntimeList, DaemonError>(new JavaRuntimeList(merged.ToImmutable()));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Result.Err<JavaRuntimeList, DaemonError>(
                new InternalDaemonError("system.java_unavailable", "Java runtime information is unavailable."));
        }
    }

    private async Task<Result<ImmutableArray<JavaRuntime>, DaemonError>> ListContributedJavaRuntimesAsync(
        CancellationToken cancellationToken)
    {
        if (extensionPointRuntimeRegistry is null)
            return Result.Ok<ImmutableArray<JavaRuntime>, DaemonError>(ImmutableArray<JavaRuntime>.Empty);

        var registrations = extensionPointRuntimeRegistry.List("provider.java");
        if (registrations.Length == 0)
            return Result.Ok<ImmutableArray<JavaRuntime>, DaemonError>(ImmutableArray<JavaRuntime>.Empty);

        var builder = ImmutableArray.CreateBuilder<JavaRuntime>();
        foreach (var registration in registrations)
        {
            if (registration.Implementation is not IPluginJavaRuntimeProvider provider)
                continue;

            var result = await provider.ListAsync(cancellationToken).ConfigureAwait(false);
            if (result.IsErr(out var error))
                return Result.Err<ImmutableArray<JavaRuntime>, DaemonError>(error!);

            foreach (var runtime in result.Unwrap())
            {
                if (string.IsNullOrWhiteSpace(runtime.JavaHome) && string.IsNullOrWhiteSpace(runtime.ExecutablePath))
                {
                    return Result.Err<ImmutableArray<JavaRuntime>, DaemonError>(
                        new InternalDaemonError("system.java_provider_invalid", $"Plugin '{registration.Owner.Id}' returned a Java runtime without a path."));
                }

                if (string.IsNullOrWhiteSpace(runtime.Version) || string.IsNullOrWhiteSpace(runtime.Architecture))
                {
                    return Result.Err<ImmutableArray<JavaRuntime>, DaemonError>(
                        new InternalDaemonError("system.java_provider_invalid", $"Plugin '{registration.Owner.Id}' returned a Java runtime without version or architecture."));
                }

                builder.Add(new JavaRuntime(
                    string.IsNullOrWhiteSpace(runtime.ExecutablePath) ? runtime.JavaHome : runtime.ExecutablePath,
                    runtime.Version,
                    runtime.Architecture));
            }
        }

        return Result.Ok<ImmutableArray<JavaRuntime>, DaemonError>(builder.ToImmutable());
    }
}
