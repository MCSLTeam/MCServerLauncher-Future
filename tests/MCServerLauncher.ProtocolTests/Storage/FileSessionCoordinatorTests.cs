using System.Collections.Immutable;
using System.Security.Cryptography;
using MCServerLauncher.Common.Contracts.Files;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.Storage;
using RustyOptions;

namespace MCServerLauncher.ProtocolTests;

public sealed class FileSessionCoordinatorTests
{
    [Fact]
    [Trait("Category", "FileSessionCoordinator")]
    public async Task Upload_StagesUntilExactContentAndHashThenAtomicallyReplacesTarget()
    {
        var fixture = CreateFixture("upload");
        var coordinator = new FileSessionCoordinator();
        var relativePath = Path.Combine(fixture.RelativePath, "target.bin");
        var targetPath = FileManager.ResolveAndValidatePath(relativePath);
        var content = new byte[] { 0x4d, 0x43, 0x53, 0x4c, 0x00, 0xff };

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            await File.WriteAllTextAsync(targetPath, "old-content");

            var opened = AssertOk(await coordinator.OpenUploadAsync(
                new UploadOpenRequest(relativePath, content.Length, Sha256(content)),
                CancellationToken.None));
            var conflicting = await coordinator.OpenUploadAsync(
                new UploadOpenRequest(relativePath, content.Length, Sha256(content)),
                CancellationToken.None);
            Assert.True(conflicting.IsErr(out _));
            Assert.IsType<ConflictDaemonError>(conflicting.UnwrapErr());

            AssertOk(await coordinator.WriteUploadChunkAsync(
                new UploadChunkRequest(opened.SessionId, 0, ImmutableArray.CreateRange(content.AsSpan(0, 3).ToArray())),
                CancellationToken.None));

            var incompleteClose = await coordinator.CloseUploadAsync(opened.SessionId, CancellationToken.None);
            Assert.True(incompleteClose.IsErr(out _));
            var incomplete = incompleteClose.UnwrapErr();
            Assert.Equal("file.upload.incomplete", incomplete.Code);
            Assert.Equal("old-content", await File.ReadAllTextAsync(targetPath));

            AssertOk(await coordinator.WriteUploadChunkAsync(
                new UploadChunkRequest(opened.SessionId, 3, ImmutableArray.CreateRange(content.AsSpan(3).ToArray())),
                CancellationToken.None));
            AssertOk(await coordinator.CloseUploadAsync(opened.SessionId, CancellationToken.None));

            Assert.Equal(content, await File.ReadAllBytesAsync(targetPath));
        }
        finally
        {
            await coordinator.StopAsync();
            Cleanup(fixture.ResolvedPath);
        }
    }

    [Trait("Category", "FileSessionCoordinator")]
    [Theory]
    [InlineData("gap", 3)]
    [InlineData("duplicate", 0)]
    [InlineData("out-of-order", 2)]
    public async Task Upload_NonContinuousChunkOffsetTerminatesSessionAndRemovesStaging(string scenario, long invalidOffset)
    {
        var fixture = CreateFixture(scenario);
        var coordinator = new FileSessionCoordinator();
        var relativePath = Path.Combine(fixture.RelativePath, "target.bin");
        var targetPath = FileManager.ResolveAndValidatePath(relativePath);
        var content = new byte[] { 1, 2, 3, 4 };

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            await File.WriteAllTextAsync(targetPath, "old-content");
            var opened = AssertOk(await coordinator.OpenUploadAsync(
                new UploadOpenRequest(relativePath, content.Length, Sha256(content)),
                CancellationToken.None));

            if (scenario is not "out-of-order")
            {
                AssertOk(await coordinator.WriteUploadChunkAsync(
                    new UploadChunkRequest(opened.SessionId, 0, ImmutableArray.Create<byte>(content[0], content[1])),
                    CancellationToken.None));
            }

            var rejected = await coordinator.WriteUploadChunkAsync(
                new UploadChunkRequest(opened.SessionId, invalidOffset, ImmutableArray.Create(content[2])),
                CancellationToken.None);

            Assert.True(rejected.IsErr(out _));
            Assert.Equal("file.chunk.offset.invalid", rejected.UnwrapErr().Code);
            AssertSessionNotFound(await coordinator.WriteUploadChunkAsync(
                new UploadChunkRequest(opened.SessionId, 0, ImmutableArray.Create<byte>(content[0])),
                CancellationToken.None));
            AssertSessionNotFound(await coordinator.CloseUploadAsync(opened.SessionId, CancellationToken.None));
            Assert.Equal("old-content", await File.ReadAllTextAsync(targetPath));
            Assert.Empty(Directory.EnumerateFiles(fixture.ResolvedPath, ".*.upload.tmp"));
        }
        finally
        {
            await coordinator.StopAsync();
            Cleanup(fixture.ResolvedPath);
        }
    }

    [Fact]
    [Trait("Category", "FileSessionCoordinator")]
    public async Task Upload_HashFailureAndExpiryPreserveTargetAndRemoveStaging()
    {
        var fixture = CreateFixture("expiry");
        var time = new ManualTimeProvider(DateTimeOffset.Parse("2026-07-11T00:00:00Z"));
        var coordinator = new FileSessionCoordinator(time);
        var relativePath = Path.Combine(fixture.RelativePath, "target.bin");
        var targetPath = FileManager.ResolveAndValidatePath(relativePath);
        var content = new byte[] { 1, 2, 3, 4 };

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            await File.WriteAllTextAsync(targetPath, "old-content");

            var badHashUpload = AssertOk(await coordinator.OpenUploadAsync(
                new UploadOpenRequest(relativePath, content.Length, new string('0', 64)),
                CancellationToken.None));
            AssertOk(await coordinator.WriteUploadChunkAsync(
                new UploadChunkRequest(badHashUpload.SessionId, 0, ImmutableArray.CreateRange(content)),
                CancellationToken.None));
            var badClose = await coordinator.CloseUploadAsync(badHashUpload.SessionId, CancellationToken.None);
            Assert.True(badClose.IsErr(out _));
            var hashError = badClose.UnwrapErr();
            Assert.Equal("file.upload.hash_mismatch", hashError.Code);
            Assert.Equal("old-content", await File.ReadAllTextAsync(targetPath));

            var expiringUpload = AssertOk(await coordinator.OpenUploadAsync(
                new UploadOpenRequest(relativePath, 0, Sha256([])),
                CancellationToken.None));
            time.Advance(FileSessionCoordinator.SessionLifetime);
            await coordinator.CleanupExpiredAsync();
            var expiredClose = await coordinator.CloseUploadAsync(expiringUpload.SessionId, CancellationToken.None);
            Assert.True(expiredClose.IsErr(out _));
            var expiryError = expiredClose.UnwrapErr();
            Assert.Equal("file.session.not_found", expiryError.Code);
            Assert.Equal("old-content", await File.ReadAllTextAsync(targetPath));
            Assert.Empty(Directory.EnumerateFiles(fixture.ResolvedPath, ".*.upload.tmp"));
        }
        finally
        {
            await coordinator.StopAsync();
            Cleanup(fixture.ResolvedPath);
        }
    }

    [Fact]
    [Trait("Category", "FileSessionCoordinator")]
    public async Task StopAsync_CleansOpenSessionsAndMakesSessionOperationsNotFound()
    {
        var fixture = CreateFixture("stop");
        var coordinator = new FileSessionCoordinator();
        var uploadPath = Path.Combine(fixture.RelativePath, "upload.bin");
        var downloadPath = Path.Combine(fixture.RelativePath, "download.bin");
        var resolvedDownloadPath = FileManager.ResolveAndValidatePath(downloadPath);
        var movedDownloadPath = FileManager.ResolveAndValidatePath(Path.Combine(fixture.RelativePath, "download-moved.bin"));
        var uploadContent = new byte[] { 1, 2, 3, 4 };

        try
        {
            Directory.CreateDirectory(fixture.ResolvedPath);
            await File.WriteAllBytesAsync(resolvedDownloadPath, uploadContent);
            var upload = AssertOk(await coordinator.OpenUploadAsync(
                new UploadOpenRequest(uploadPath, uploadContent.Length, Sha256(uploadContent)),
                CancellationToken.None));
            var download = AssertOk(await coordinator.OpenDownloadAsync(
                new DownloadOpenRequest(downloadPath),
                CancellationToken.None));

            await coordinator.StopAsync();

            Assert.Empty(Directory.EnumerateFiles(fixture.ResolvedPath, ".*.upload.tmp"));
            File.Move(resolvedDownloadPath, movedDownloadPath);

            AssertSessionNotFound(await coordinator.WriteUploadChunkAsync(
                new UploadChunkRequest(upload.SessionId, 0, ImmutableArray.CreateRange(uploadContent)),
                CancellationToken.None));
            AssertSessionNotFound(await coordinator.ReadDownloadChunkAsync(
                new DownloadChunkRequest(download.SessionId, 0, uploadContent.Length),
                CancellationToken.None));
            AssertCoordinatorStopped(await coordinator.OpenUploadAsync(
                new UploadOpenRequest(Path.Combine(fixture.RelativePath, "next-upload.bin"), 0, Sha256([])),
                CancellationToken.None));
            AssertCoordinatorStopped(await coordinator.OpenDownloadAsync(
                new DownloadOpenRequest(Path.Combine(fixture.RelativePath, "download-moved.bin")),
                CancellationToken.None));
            AssertCoordinatorStopped(await coordinator.CreateDirectoryAsync(
                new PathRequest(Path.Combine(fixture.RelativePath, "new-directory")),
                CancellationToken.None));
        }
        finally
        {
            await coordinator.StopAsync();
            Cleanup(fixture.ResolvedPath);
        }
    }

    [Fact]
    [Trait("Category", "FileSessionCoordinator")]
    public async Task UploadCleanupFailure_ReleasesLeaseWithoutMaskingAdmissionOrCancellation()
    {
        var fixture = CreateFixture("cleanup-failure");
        var relativePath = Path.Combine(fixture.RelativePath, "target.bin");
        var coordinator = new FileSessionCoordinator(
            deleteUploadStaging: _ => throw new IOException("injected staging cleanup failure"),
            onUploadStagingCreatedAsync: _ => throw new IOException("injected admission failure"));

        try
        {
            var admissionFailure = await coordinator.OpenUploadAsync(
                new UploadOpenRequest(relativePath, 1, Sha256([1])),
                CancellationToken.None);
            Assert.True(admissionFailure.IsErr(out _));
            Assert.Equal("file.storage_failed", admissionFailure.UnwrapErr().Code);

            var retryFailure = await coordinator.OpenUploadAsync(
                new UploadOpenRequest(relativePath, 1, Sha256([1])),
                CancellationToken.None);
            Assert.True(retryFailure.IsErr(out _));
            Assert.NotEqual("file.upload.active", retryFailure.UnwrapErr().Code);
        }
        finally
        {
            await coordinator.StopAsync();
            Cleanup(fixture.ResolvedPath);
        }
    }

    [Fact]
    [Trait("Category", "FileSessionCoordinator")]
    public async Task StopAsync_WhenUploadStagingCleanupFails_ContinuesClosingRemainingSessions()
    {
        var fixture = CreateFixture("stop-cleanup-failure");
        var coordinator = new FileSessionCoordinator(
            deleteUploadStaging: _ => throw new IOException("injected staging cleanup failure"));
        var uploadPath = Path.Combine(fixture.RelativePath, "upload.bin");
        var downloadPath = Path.Combine(fixture.RelativePath, "download.bin");
        var resolvedDownloadPath = FileManager.ResolveAndValidatePath(downloadPath);
        var movedDownloadPath = FileManager.ResolveAndValidatePath(Path.Combine(fixture.RelativePath, "download-moved.bin"));
        var content = new byte[] { 1, 2, 3, 4 };

        try
        {
            Directory.CreateDirectory(fixture.ResolvedPath);
            await File.WriteAllBytesAsync(resolvedDownloadPath, content);
            _ = AssertOk(await coordinator.OpenUploadAsync(
                new UploadOpenRequest(uploadPath, content.Length, Sha256(content)),
                CancellationToken.None));
            _ = AssertOk(await coordinator.OpenDownloadAsync(
                new DownloadOpenRequest(downloadPath),
                CancellationToken.None));

            await coordinator.StopAsync();

            File.Move(resolvedDownloadPath, movedDownloadPath);
            AssertCoordinatorStopped(await coordinator.OpenUploadAsync(
                new UploadOpenRequest(Path.Combine(fixture.RelativePath, "next.bin"), 0, Sha256([])),
                CancellationToken.None));
        }
        finally
        {
            await coordinator.StopAsync();
            Cleanup(fixture.ResolvedPath);
        }
    }

    [Fact]
    [Trait("Category", "FileSessionCoordinator")]
    public async Task FileOperationErrors_DoNotExposeDaemonRootOrResolvedPaths()
    {
        var fixture = CreateFixture("error-redaction");
        var coordinator = new FileSessionCoordinator();

        try
        {
            Directory.CreateDirectory(fixture.ResolvedPath);
            var missing = await coordinator.OpenDownloadAsync(
                new DownloadOpenRequest(Path.Combine(fixture.RelativePath, "missing.bin")),
                CancellationToken.None);
            var denied = await coordinator.OpenDownloadAsync(
                new DownloadOpenRequest(fixture.RelativePath),
                CancellationToken.None);

            Assert.True(missing.IsErr(out _));
            Assert.True(denied.IsErr(out _));
            Assert.DoesNotContain(FileManager.Root, missing.UnwrapErr().Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(FileManager.Root, denied.UnwrapErr().Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(fixture.ResolvedPath, missing.UnwrapErr().Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(fixture.ResolvedPath, denied.UnwrapErr().Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await coordinator.StopAsync();
            Cleanup(fixture.ResolvedPath);
        }
    }

    [Fact]
    [Trait("Category", "FileSessionCoordinator")]
    public void PathValidation_RejectsPrefixSiblingOutsideConfiguredRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mcsl-root-{Guid.NewGuid():N}");
        var sibling = root + "-sibling";
        try
        {
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(sibling);
            Assert.Throws<IOException>(() => FileSessionCoordinator.ResolveAndValidatePath(sibling, root));
        }
        finally
        {
            Cleanup(root);
            Cleanup(sibling);
        }
    }

    [Fact]
    [Trait("Category", "FileSessionCoordinator")]
    public void PathValidation_RejectsReparsePointBelowConfiguredRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mcsl-root-{Guid.NewGuid():N}");
        var outside = Path.Combine(Path.GetTempPath(), $"mcsl-outside-{Guid.NewGuid():N}");
        var link = Path.Combine(root, "outside-link");
        try
        {
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(outside);
            if (!TryCreateDirectorySymbolicLink(link, outside))
                return;

            Assert.True(File.GetAttributes(link).HasFlag(FileAttributes.ReparsePoint));
            Assert.Equal(Path.GetFullPath(root), FileSessionCoordinator.ResolveAndValidatePath(root, root));
            Assert.Throws<IOException>(() => FileSessionCoordinator.ResolveAndValidatePath(
                Path.Combine(link, "does-not-exist", "target.bin"),
                root));
        }
        finally
        {
            Cleanup(root);
            Cleanup(outside);
        }
    }

    [Fact]
    [Trait("Category", "FileSessionCoordinator")]
    public async Task CopyDirectory_RejectsSourceFileReparsePoint()
    {
        var fixture = CreateFixture("copy-source-reparse");
        var outside = Path.Combine(Path.GetTempPath(), $"mcsl-copy-outside-{Guid.NewGuid():N}");
        var coordinator = new FileSessionCoordinator();
        var sourceRelativePath = Path.Combine(fixture.RelativePath, "source");
        var destinationRelativePath = Path.Combine(fixture.RelativePath, "destination");
        var sourcePath = FileManager.ResolveAndValidatePath(sourceRelativePath);
        var linkedFile = Path.Combine(sourcePath, "linked.bin");
        var outsideFile = Path.Combine(outside, "outside.bin");

        try
        {
            Directory.CreateDirectory(sourcePath);
            Directory.CreateDirectory(outside);
            await File.WriteAllBytesAsync(outsideFile, [1, 2, 3]);
            if (!TryCreateFileSymbolicLink(linkedFile, outsideFile))
                return;

            var result = await coordinator.CopyDirectoryAsync(
                new PathTransferRequest(sourceRelativePath, destinationRelativePath),
                CancellationToken.None);

            Assert.True(result.IsErr(out _));
            Assert.Equal("file.path.invalid", result.UnwrapErr().Code);
            Assert.False(File.Exists(FileManager.ResolveAndValidatePath(
                Path.Combine(destinationRelativePath, "linked.bin"))));
        }
        finally
        {
            await coordinator.StopAsync();
            Cleanup(fixture.ResolvedPath);
            Cleanup(outside);
        }
    }

    [Fact]
    [Trait("Category", "FileSessionCoordinator")]
    public async Task CopyDirectory_RejectsPreexistingDestinationDirectoryReparsePoint()
    {
        var fixture = CreateFixture("copy-destination-reparse");
        var outside = Path.Combine(Path.GetTempPath(), $"mcsl-copy-outside-{Guid.NewGuid():N}");
        var coordinator = new FileSessionCoordinator();
        var sourceRelativePath = Path.Combine(fixture.RelativePath, "source");
        var destinationRelativePath = Path.Combine(fixture.RelativePath, "destination");
        var sourceNestedPath = FileManager.ResolveAndValidatePath(Path.Combine(sourceRelativePath, "nested"));
        var destinationPath = FileManager.ResolveAndValidatePath(destinationRelativePath);
        var destinationLink = Path.Combine(destinationPath, "nested");

        try
        {
            Directory.CreateDirectory(sourceNestedPath);
            Directory.CreateDirectory(destinationPath);
            Directory.CreateDirectory(outside);
            await File.WriteAllBytesAsync(Path.Combine(sourceNestedPath, "payload.bin"), [1, 2, 3]);
            if (!TryCreateDirectorySymbolicLink(destinationLink, outside))
                return;

            var result = await coordinator.CopyDirectoryAsync(
                new PathTransferRequest(sourceRelativePath, destinationRelativePath),
                CancellationToken.None);

            Assert.True(result.IsErr(out _));
            Assert.Equal("file.path.invalid", result.UnwrapErr().Code);
            Assert.False(File.Exists(Path.Combine(outside, "payload.bin")));
        }
        finally
        {
            await coordinator.StopAsync();
            Cleanup(fixture.ResolvedPath);
            Cleanup(outside);
        }
    }

    [Fact]
    [Trait("Category", "FileSessionCoordinator")]
    public async Task Download_ConcurrentSessionsRespectConfiguredLimit()
    {
        var fixture = CreateFixture("download-limit");
        var coordinator = new FileSessionCoordinator(downloadSessionLimit: 1);
        var relativePath = Path.Combine(fixture.RelativePath, "target.bin");
        var targetPath = FileManager.ResolveAndValidatePath(relativePath);

        try
        {
            Directory.CreateDirectory(fixture.ResolvedPath);
            await File.WriteAllBytesAsync(targetPath, [1, 2, 3]);

            var first = AssertOk(await coordinator.OpenDownloadAsync(
                new DownloadOpenRequest(relativePath),
                CancellationToken.None));
            var second = await coordinator.OpenDownloadAsync(
                new DownloadOpenRequest(relativePath),
                CancellationToken.None);

            Assert.True(second.IsErr(out _));
            Assert.Equal("file.download.limit", second.UnwrapErr().Code);
            AssertOk(await coordinator.CloseDownloadAsync(first.SessionId, CancellationToken.None));
        }
        finally
        {
            await coordinator.StopAsync();
            Cleanup(fixture.ResolvedPath);
        }
    }

    [Fact]
    [Trait("Category", "FileSessionCoordinator")]
    public async Task Download_HashFaultDisposesOpenedStream()
    {
        var fixture = CreateFixture("download-hash-fault");
        Microsoft.Win32.SafeHandles.SafeFileHandle? observedHandle = null;
        var coordinator = new FileSessionCoordinator(hashAsync: (stream, _, _) =>
        {
            observedHandle = Assert.IsType<FileStream>(stream).SafeFileHandle;
            throw new IOException("Injected hash failure.");
        });
        var relativePath = Path.Combine(fixture.RelativePath, "target.bin");
        var targetPath = FileManager.ResolveAndValidatePath(relativePath);
        var movedPath = FileManager.ResolveAndValidatePath(Path.Combine(fixture.RelativePath, "moved.bin"));

        try
        {
            Directory.CreateDirectory(fixture.ResolvedPath);
            await File.WriteAllBytesAsync(targetPath, [1, 2, 3]);

            var result = await coordinator.OpenDownloadAsync(new DownloadOpenRequest(relativePath), CancellationToken.None);

            Assert.True(result.IsErr(out _));
            Assert.Equal("file.storage_failed", result.UnwrapErr().Code);
            Assert.NotNull(observedHandle);
            Assert.True(observedHandle.IsClosed);
            File.Move(targetPath, movedPath);
        }
        finally
        {
            await coordinator.StopAsync();
            Cleanup(fixture.ResolvedPath);
        }
    }

    [Fact]
    [Trait("Category", "FileSessionCoordinator")]
    public async Task Download_CancellationDuringHashDisposesOpenedStream()
    {
        var fixture = CreateFixture("download-cancellation");
        Microsoft.Win32.SafeHandles.SafeFileHandle? observedHandle = null;
        using var cancellationSource = new CancellationTokenSource();
        var coordinator = new FileSessionCoordinator(hashAsync: (stream, _, _) =>
        {
            observedHandle = Assert.IsType<FileStream>(stream).SafeFileHandle;
            cancellationSource.Cancel();
            throw new OperationCanceledException(cancellationSource.Token);
        });
        var relativePath = Path.Combine(fixture.RelativePath, "target.bin");
        var targetPath = FileManager.ResolveAndValidatePath(relativePath);
        var movedPath = FileManager.ResolveAndValidatePath(Path.Combine(fixture.RelativePath, "moved.bin"));

        try
        {
            Directory.CreateDirectory(fixture.ResolvedPath);
            await File.WriteAllBytesAsync(targetPath, [1, 2, 3]);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => coordinator.OpenDownloadAsync(
                new DownloadOpenRequest(relativePath),
                cancellationSource.Token));

            Assert.NotNull(observedHandle);
            Assert.True(observedHandle.IsClosed);
            File.Move(targetPath, movedPath);
        }
        finally
        {
            await coordinator.StopAsync();
            Cleanup(fixture.ResolvedPath);
        }
    }

    private static T AssertOk<T>(Result<T, DaemonError> result)
        where T : notnull
    {
        if (result.IsOk(out var value))
            return value;

        throw new Xunit.Sdk.XunitException(result.UnwrapErr().Message);
    }

    private static void AssertSessionNotFound<T>(Result<T, DaemonError> result)
        where T : notnull
    {
        Assert.True(result.IsErr(out _));
        Assert.Equal("file.session.not_found", result.UnwrapErr().Code);
    }

    private static void AssertCoordinatorStopped<T>(Result<T, DaemonError> result)
        where T : notnull
    {
        Assert.True(result.IsErr(out _));
        var error = result.UnwrapErr();
        Assert.IsType<ConflictDaemonError>(error);
        Assert.Equal("file.session.stopped", error.Code);
    }

    private static string Sha256(byte[] value) => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    private static Fixture CreateFixture(string name)
    {
        var relativePath = Path.Combine("caches", $"file-session-{name}-{Guid.NewGuid():N}");
        return new Fixture(relativePath, FileManager.ResolveAndValidatePath(relativePath));
    }

    private static void Cleanup(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    private static bool TryCreateDirectorySymbolicLink(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (PlatformNotSupportedException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            // The test uses fresh paths; on platforms where link creation is unavailable,
            // the expected result is an I/O failure rather than a false security assertion.
            return false;
        }
    }

    private static bool TryCreateFileSymbolicLink(string link, string target)
    {
        try
        {
            File.CreateSymbolicLink(link, target);
            return true;
        }
        catch (PlatformNotSupportedException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private readonly record struct Fixture(string RelativePath, string ResolvedPath);

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan duration) => _now += duration;
    }
}
