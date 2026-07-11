using MCServerLauncher.Common.Contracts.Files;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.ApplicationCore;
using MCServerLauncher.Daemon.Storage;
using MCServerLauncher.Daemon.Utils.LazyCell;
using RustyOptions;
using LegacyJavaRuntime = MCServerLauncher.Common.ProtoType.JavaInfo;
using LegacyCpuInfo = MCServerLauncher.Common.ProtoType.Status.CpuInfo;
using LegacyDriveInfo = MCServerLauncher.Common.ProtoType.Status.DriveInformation;
using LegacyMemInfo = MCServerLauncher.Common.ProtoType.Status.MemInfo;
using LegacyOsInfo = MCServerLauncher.Common.ProtoType.Status.OsInfo;
using LegacySystemInfo = MCServerLauncher.Common.ProtoType.Status.SystemInfo;

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
    public async Task LocalSystemApplication_MapsLegacyCellsToContracts()
    {
        var systemInfo = new LegacySystemInfo(
            new LegacyOsInfo("TestOS", "x64"),
            new LegacyCpuInfo("vendor", "processor", 8, 12.5, 4, 8),
            new LegacyMemInfo(4096, 1024),
            new LegacyDriveInfo("NTFS", 1_000, 400, "C:"),
            [new LegacyDriveInfo("ext4", 2_000, 1_500, "/data")],
            "test-daemon");
        LegacyJavaRuntime[] runtimes =
        [
            new LegacyJavaRuntime("java-17", "17.0.13", "x64"),
            new LegacyJavaRuntime("java-21", "21.0.5", "arm64")
        ];
        var application = new LocalSystemApplication(
            new ControlledAsyncTimedLazyCell<LegacySystemInfo>(Task.FromResult(systemInfo)),
            new ControlledAsyncTimedLazyCell<LegacyJavaRuntime[]>(Task.FromResult(runtimes)));

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
        var pendingSystemInfo = new TaskCompletionSource<LegacySystemInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        var application = new LocalSystemApplication(
            new ControlledAsyncTimedLazyCell<LegacySystemInfo>(pendingSystemInfo.Task),
            new ControlledAsyncTimedLazyCell<LegacyJavaRuntime[]>(Task.FromException<LegacyJavaRuntime[]>(
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
