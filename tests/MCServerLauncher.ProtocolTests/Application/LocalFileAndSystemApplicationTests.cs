using MCServerLauncher.Common.Contracts.Files;
using MCServerLauncher.Common.Contracts.System;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.ApplicationCore;
using MCServerLauncher.Daemon.Storage;
using MCServerLauncher.Daemon.Utils.LazyCell;
using RustyOptions;

namespace MCServerLauncher.ProtocolTests;

public sealed class LocalFileAndSystemApplicationTests
{
    [Fact]
    public async Task LocalFileApplication_UsesProvidedCoordinatorForPathResultsAndFailureState()
    {
        var relativePath = Path.Combine("caches", $"local-file-application-{Guid.NewGuid():N}");
        var resolvedPath = FileManager.ResolveAndValidatePath(relativePath);
        var coordinator = new FileSessionCoordinator();
        var application = new LocalFileApplication(coordinator);
        try
        {
            var created = await application.CreateDirectoryAsync(new PathRequest(relativePath), CancellationToken.None);
            Assert.True(created.IsOk(out _));

            var visibleThroughCoordinator = await coordinator.GetDirectoryInfoAsync(
                new PathRequest(relativePath),
                CancellationToken.None);
            Assert.True(visibleThroughCoordinator.IsOk(out _));

            using var cancellationSource = new CancellationTokenSource();
            cancellationSource.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => application.GetDirectoryInfoAsync(
                new PathRequest(relativePath),
                cancellationSource.Token));

            var invalidPath = await application.CreateDirectoryAsync(new PathRequest(".."), CancellationToken.None);
            Assert.True(invalidPath.IsErr(out var invalidPathError));
            Assert.IsType<ValidationDaemonError>(invalidPathError);
            Assert.Equal("file.path.invalid", invalidPathError.Code);

            await coordinator.StopAsync();
            var stopped = await application.CreateDirectoryAsync(new PathRequest(relativePath), CancellationToken.None);
            Assert.True(stopped.IsErr(out var stoppedError));
            Assert.IsType<ConflictDaemonError>(stoppedError);
            Assert.Equal("file.session.stopped", stoppedError.Code);
        }
        finally
        {
            await coordinator.StopAsync();
            if (Directory.Exists(resolvedPath))
                Directory.Delete(resolvedPath, recursive: true);
        }
    }

    [Fact]
    public async Task LocalSystemApplication_MapsCellsToContracts()
    {
        var systemInfo = new SystemInfo(
            new OperatingSystemInfo("TestOS", "x64"),
            new ProcessorInfo("vendor", "processor", 8, 12.5, 4, 8),
            new MemoryInfo(4096, 1024),
            new MCServerLauncher.Common.Contracts.System.DriveInfo("NTFS", 1_000, 400, "C:"),
            [new MCServerLauncher.Common.Contracts.System.DriveInfo("ext4", 2_000, 1_500, "/data")],
            "test-daemon");
        JavaRuntime[] runtimes =
        [
            new JavaRuntime("java-17", "17.0.13", "x64"),
            new JavaRuntime("java-21", "21.0.5", "arm64")
        ];
        var application = new LocalSystemApplication(
            new ControlledAsyncTimedLazyCell<SystemInfo>(Task.FromResult(systemInfo)),
            new ControlledAsyncTimedLazyCell<JavaRuntime[]>(Task.FromResult(runtimes)));

        var systemResult = await application.GetSystemInfoAsync(CancellationToken.None);
        var javaResult = await application.ListJavaRuntimesAsync(CancellationToken.None);

        Assert.True(systemResult.IsOk(out var mappedSystem));
        Assert.Equal("TestOS", mappedSystem.Os.Name);
        Assert.Equal(4, mappedSystem.Cpu.CoreCount);
        Assert.Equal(8, mappedSystem.Cpu.ThreadCount);
        Assert.Equal("/data", Assert.Single(mappedSystem.Drives).Name);
        Assert.Equal("test-daemon", mappedSystem.DaemonVersion);

        Assert.True(javaResult.IsOk(out var mappedRuntimes));
        Assert.Equal(runtimes.Select(runtime => runtime.Path), mappedRuntimes.Items.Select(runtime => runtime.Path));
        Assert.Equal(runtimes.Select(runtime => runtime.Architecture), mappedRuntimes.Items.Select(runtime => runtime.Architecture));
    }

    [Fact]
    public async Task LocalSystemApplication_PropagatesCallerCancellationAndMapsCellFailures()
    {
        var pendingSystemInfo = new TaskCompletionSource<SystemInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        var application = new LocalSystemApplication(
            new ControlledAsyncTimedLazyCell<SystemInfo>(pendingSystemInfo.Task),
            new ControlledAsyncTimedLazyCell<JavaRuntime[]>(Task.FromException<JavaRuntime[]>(
                new IOException("Java scan failed."))));
        using var cancellationSource = new CancellationTokenSource();

        var pendingCall = application.GetSystemInfoAsync(cancellationSource.Token);
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pendingCall);

        var failedJavaResult = await application.ListJavaRuntimesAsync(CancellationToken.None);
        Assert.True(failedJavaResult.IsErr(out var error));
        Assert.IsType<InternalDaemonError>(error);
        Assert.Equal("system.java_unavailable", error.Code);
    }

    private sealed class ControlledAsyncTimedLazyCell<T>(Task<T> value) : IAsyncTimedLazyCell<T>
    {
        public ValueTask<T> Value => new(value);
        public DateTime LastUpdated => DateTime.UnixEpoch;
        public TimeSpan CacheDuration => Timeout.InfiniteTimeSpan;
        public bool IsExpired() => false;
        public Task Update() => Task.CompletedTask;
    }
}
