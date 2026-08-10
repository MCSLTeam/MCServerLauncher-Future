using System.Collections.Frozen;
using System.Collections.Immutable;
using MCServerLauncher.Common.Contracts.Files;
using MCServerLauncher.Common.Contracts.System;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Plugins;
using MCServerLauncher.Daemon.API.Protocol;
using MCServerLauncher.Daemon.ApplicationCore;
using MCServerLauncher.Daemon.Plugins;
using MCServerLauncher.Daemon.Storage;
using MCServerLauncher.Daemon.Utils.LazyCell;
using NuGet.Versioning;
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
        var runtimeList = new JavaRuntimeList(runtimes.ToImmutableArray());
        var application = new LocalSystemApplication(
            new ControlledAsyncTimedLazyCell<SystemInfo>(Task.FromResult(systemInfo)),
            new ControlledAsyncTimedLazyCell<JavaRuntimeList>(Task.FromResult(runtimeList)));

        var systemResult = await application.GetSystemInfoAsync(CancellationToken.None);
        var javaResult = await application.ListJavaRuntimesAsync(CancellationToken.None);

        Assert.True(systemResult.IsOk(out var mappedSystem));
        Assert.Equal("TestOS", mappedSystem.Os.Name);
        Assert.Equal(4, mappedSystem.Cpu.CoreCount);
        Assert.Equal(8, mappedSystem.Cpu.ThreadCount);
        Assert.Equal("/data", Assert.Single(mappedSystem.Drives).Name);
        Assert.Equal("test-daemon", mappedSystem.DaemonVersion);

        Assert.True(javaResult.IsOk(out var mappedRuntimes));
        Assert.Same(runtimeList, mappedRuntimes);
        Assert.Equal(runtimes.Select(runtime => runtime.Path), mappedRuntimes.Items.Select(runtime => runtime.Path));
        Assert.Equal(runtimes.Select(runtime => runtime.Architecture), mappedRuntimes.Items.Select(runtime => runtime.Architecture));
    }

    [Fact]
    public async Task LocalSystemApplication_MergesDeclaredPluginJavaProviderRuntimes()
    {
        var systemInfo = new SystemInfo(
            new OperatingSystemInfo("TestOS", "x64"),
            new ProcessorInfo("vendor", "processor", 8, 12.5, 4, 8),
            new MemoryInfo(4096, 1024),
            new MCServerLauncher.Common.Contracts.System.DriveInfo("NTFS", 1_000, 400, "C:"),
            [],
            "test-daemon");
        var runtimeList = new JavaRuntimeList([
            new JavaRuntime("java-17", "17.0.13", "x64")
        ]);
        var registry = new PluginExtensionPointRuntimeRegistry();
        var manifest = CreatePluginManifest([
            new PluginManifestExtensionPoint(
                "provider.java",
                PluginExtensionPointKind.Provider,
                20,
                null,
                ImmutableArray<string>.Empty,
                null)
        ]);
        var registered = registry
            .CreateRegistrar(manifest, new PluginErrorFactory(manifest.Identity))
            .Register<IPluginJavaRuntimeProvider>("provider.java", new TestJavaProvider());
        Assert.True(registered.IsOk(out _));
        var application = new LocalSystemApplication(
            new ControlledAsyncTimedLazyCell<SystemInfo>(Task.FromResult(systemInfo)),
            new ControlledAsyncTimedLazyCell<JavaRuntimeList>(Task.FromResult(runtimeList)),
            registry);

        var javaResult = await application.ListJavaRuntimesAsync(CancellationToken.None);

        Assert.True(javaResult.IsOk(out var mappedRuntimes));
        Assert.Equal(
            ["java-17", "/plugin/java/bin/java"],
            mappedRuntimes.Items.Select(static runtime => runtime.Path).ToArray());
        Assert.Equal("21.0.7", mappedRuntimes.Items[1].Version);
        Assert.Equal("arm64", mappedRuntimes.Items[1].Architecture);
    }

    [Fact]
    public async Task LocalSystemApplication_PropagatesCallerCancellationAndMapsCellFailures()
    {
        var pendingSystemInfo = new TaskCompletionSource<SystemInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        var application = new LocalSystemApplication(
            new ControlledAsyncTimedLazyCell<SystemInfo>(pendingSystemInfo.Task),
            new ControlledAsyncTimedLazyCell<JavaRuntimeList>(Task.FromException<JavaRuntimeList>(
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

    private static PluginManifest CreatePluginManifest(ImmutableArray<PluginManifestExtensionPoint> extensionPoints) =>
        new(
            new PluginIdentity("community.example.java", "1.0.0"),
            "Runtime.dll",
            "Community.Example.Java.Plugin",
            NuGetVersion.Parse("1.0.0"),
            VersionRange.Parse("[1.0.0,2.0.0)"),
            Array.Empty<PluginFeature>().ToFrozenSet(),
            ImmutableArray<PluginManifestPluginDependency>.Empty,
            ImmutableArray<PluginManifestContractDependency>.Empty,
            extensionPoints,
            ImmutableArray<PluginManifestCommand>.Empty,
            "/tmp/community.example.java",
            "/tmp/community.example.java/Runtime.dll",
            "digest");

    private sealed class TestJavaProvider : IPluginJavaRuntimeProvider
    {
        public ValueTask<Result<IReadOnlyList<PluginJavaRuntimeResolution>, DaemonError>> ListAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(PluginResult.Ok<IReadOnlyList<PluginJavaRuntimeResolution>>([
                new PluginJavaRuntimeResolution("Duplicate Java", "/java-17", "java-17", "17.0.13", "x64"),
                new PluginJavaRuntimeResolution("Plugin Java", "/plugin/java", "/plugin/java/bin/java", "21.0.7", "arm64")
            ]));
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
