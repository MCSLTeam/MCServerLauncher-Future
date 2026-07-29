using MCServerLauncher.Common.Contracts.Files;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Plugins;
using MCServerLauncher.Daemon.ApplicationCore.Auth;
using MCServerLauncher.Daemon.Plugins;
using MCServerLauncher.Daemon.Storage;
using RustyOptions;

namespace MCServerLauncher.ProtocolTests;

/// <summary>
/// The daemon data root holds the audit, plan, automation, backup, operation and monitoring stores
/// beside instance data, so a plugin granted the file features must not be able to reach them
/// through the shared file application, nor hold unbounded or foreign file sessions.
/// </summary>
public sealed class PluginFileContainmentTests
{
    [Theory]
    [InlineData("audit")]
    [InlineData("plans")]
    [InlineData("automation")]
    [InlineData("backups")]
    [InlineData("operations")]
    [InlineData("monitoring")]
    [InlineData("instances/../audit")]
    [InlineData("/audit")]
    public async Task DaemonControlDirectoriesAreOutOfReach(string path)
    {
        var inner = new RecordingFileApplication();
        var contained = new ContainedPluginFileApplication(inner);

        var read = await contained.GetDirectoryInfoAsync(new PathRequest(path), CancellationToken.None);
        var delete = await contained.DeleteDirectoryAsync(
            new DeleteDirectoryRequest(path, Recursive: true),
            CancellationToken.None);

        Assert.True(read.IsErr(out var readError));
        Assert.Equal("plugin.file.out_of_containment", readError!.Code);
        Assert.True(delete.IsErr(out var deleteError));
        Assert.Equal("plugin.file.out_of_containment", deleteError!.Code);
        Assert.Equal(0, inner.CallCount);
    }

    [Theory]
    [InlineData("/")]
    [InlineData(".")]
    [InlineData("instances/..")]
    [InlineData("instances")]
    public async Task RootsThemselvesCannotBeDeleted(string path)
    {
        var inner = new RecordingFileApplication();
        var contained = new ContainedPluginFileApplication(inner);

        var result = await contained.DeleteDirectoryAsync(
            new DeleteDirectoryRequest(path, Recursive: true),
            CancellationToken.None);

        Assert.True(result.IsErr(out var error));
        Assert.Equal("plugin.file.out_of_containment", error!.Code);
        Assert.Equal(0, inner.CallCount);
    }

    [Fact]
    public async Task InstanceScopedPathsPassThrough()
    {
        var inner = new RecordingFileApplication();
        var contained = new ContainedPluginFileApplication(inner);

        var result = await contained.GetDirectoryInfoAsync(
            new PathRequest("instances/00000000-0000-0000-0000-000000000001/mods"),
            CancellationToken.None);

        Assert.True(result.IsErr(out var error));
        Assert.Equal("test.inner", error!.Code);
        Assert.Equal(1, inner.CallCount);
    }

    [Fact]
    public async Task TransferRejectsWhenEitherEndEscapes()
    {
        var inner = new RecordingFileApplication();
        var contained = new ContainedPluginFileApplication(inner);
        var inside = "instances/00000000-0000-0000-0000-000000000001/server.jar";

        var exfiltrate = await contained.CopyFileAsync(
            new PathTransferRequest("audit/audit.jsonl", inside),
            CancellationToken.None);
        var overwrite = await contained.MoveFileAsync(
            new PathTransferRequest(inside, "automation/policies.json"),
            CancellationToken.None);

        Assert.True(exfiltrate.IsErr(out var exfiltrateError));
        Assert.Equal("plugin.file.out_of_containment", exfiltrateError!.Code);
        Assert.True(overwrite.IsErr(out var overwriteError));
        Assert.Equal("plugin.file.out_of_containment", overwriteError!.Code);
        Assert.Equal(0, inner.CallCount);
    }

    [Fact]
    public async Task ConcurrentSessionsAreBounded()
    {
        var inner = new RecordingFileApplication();
        var contained = new ContainedPluginFileApplication(inner);
        var path = "instances/00000000-0000-0000-0000-000000000001/world.zip";

        for (var opened = 0; opened < ContainedPluginFileApplication.MaximumConcurrentSessions; opened++)
        {
            var admitted = await contained.OpenDownloadAsync(new DownloadOpenRequest(path), CancellationToken.None);
            Assert.True(admitted.IsOk(out _));
        }

        var refused = await contained.OpenDownloadAsync(new DownloadOpenRequest(path), CancellationToken.None);
        var refusedUpload = await contained.OpenUploadAsync(
            new UploadOpenRequest(path, 1, new string('a', 64)),
            CancellationToken.None);

        Assert.True(refused.IsErr(out var refusedError));
        Assert.Equal("plugin.file.session_limit", refusedError!.Code);
        Assert.True(refusedUpload.IsErr(out var refusedUploadError));
        Assert.Equal("plugin.file.session_limit", refusedUploadError!.Code);
    }

    [Fact]
    public async Task ForeignSessionsCannotBeReadOrClosed()
    {
        var inner = new RecordingFileApplication();
        var contained = new ContainedPluginFileApplication(inner);
        var foreign = Guid.NewGuid();

        var read = await contained.ReadDownloadChunkAsync(
            new DownloadChunkRequest(foreign, 0, 1),
            CancellationToken.None);
        var close = await contained.CloseDownloadAsync(foreign, CancellationToken.None);
        var write = await contained.WriteUploadChunkAsync(
            new UploadChunkRequest(foreign, 0, []),
            CancellationToken.None);
        var cancel = await contained.CancelUploadAsync(foreign, CancellationToken.None);

        Assert.True(read.IsErr(out var readError));
        Assert.Equal("plugin.file.session_not_owned", readError!.Code);
        Assert.True(close.IsErr(out var closeError));
        Assert.Equal("plugin.file.session_not_owned", closeError!.Code);
        Assert.True(write.IsErr(out var writeError));
        Assert.Equal("plugin.file.session_not_owned", writeError!.Code);
        Assert.True(cancel.IsErr(out var cancelError));
        Assert.Equal("plugin.file.session_not_owned", cancelError!.Code);
        Assert.Equal(0, inner.CallCount);
    }

    [Fact]
    public async Task ReleaseClosesEverySessionThePluginStillHolds()
    {
        var inner = new RecordingFileApplication();
        var contained = new ContainedPluginFileApplication(inner);
        var path = "instances/00000000-0000-0000-0000-000000000001/world.zip";
        var download = await contained.OpenDownloadAsync(new DownloadOpenRequest(path), CancellationToken.None);
        var upload = await contained.OpenUploadAsync(
            new UploadOpenRequest(path, 1, new string('a', 64)),
            CancellationToken.None);
        Assert.True(download.IsOk(out var downloadSession));
        Assert.True(upload.IsOk(out var uploadSession));

        await contained.ReleaseSessionsAsync();

        Assert.Contains(downloadSession!.SessionId, inner.ClosedDownloads);
        Assert.Contains(uploadSession!.SessionId, inner.CanceledUploads);
        var afterRelease = await contained.ReadDownloadChunkAsync(
            new DownloadChunkRequest(downloadSession.SessionId, 0, 1),
            CancellationToken.None);
        Assert.True(afterRelease.IsErr(out var afterError));
        Assert.Equal("plugin.file.session_not_owned", afterError!.Code);
    }

    [Fact]
    public async Task AuthorizerConfinesTheFileSurfaceItHandsOut()
    {
        var inner = new RecordingFileApplication();
        var identity = new PluginIdentity("community.file-containment", "1.0.0");
        var authorizer = new PluginApplicationAuthorizer(
            identity,
            ["file.read", "file.write"],
            new CallerContextFactory(new VerifiedPrincipalAuthority()),
            instanceCatalog: null,
            instanceQueries: null,
            system: null,
            instanceManagement: null,
            operationQueries: null,
            operationControl: null,
            provisioning: null,
            backups: null,
            audit: null,
            monitoring: null,
            automation: null,
            eventRules: null,
            files: inner);

        var escape = await authorizer.Host.FileWrites.DeleteDirectoryAsync(
            new DeleteDirectoryRequest("audit", Recursive: true),
            CancellationToken.None);

        Assert.True(escape.IsErr(out var error));
        Assert.Equal("plugin.file.out_of_containment", error!.Code);
        Assert.Equal(0, inner.CallCount);
    }

    private sealed class RecordingFileApplication : IFileApplication
    {
        private static readonly DaemonError Inner = new ValidationDaemonError("test.inner", "inner reached");

        internal int CallCount { get; private set; }
        internal List<Guid> ClosedDownloads { get; } = [];
        internal List<Guid> CanceledUploads { get; } = [];

        public Task<Result<DirectoryDetails, DaemonError>> GetDirectoryInfoAsync(
            PathRequest request,
            CancellationToken cancellationToken) => Fail<DirectoryDetails>();

        public Task<Result<FileDetails, DaemonError>> GetFileInfoAsync(
            PathRequest request,
            CancellationToken cancellationToken) => Fail<FileDetails>();

        public Task<Result<DownloadSession, DaemonError>> OpenDownloadAsync(
            DownloadOpenRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(Result.Ok<DownloadSession, DaemonError>(
                new DownloadSession(Guid.NewGuid(), 1, new string('a', 64), 1, DateTimeOffset.UtcNow.AddMinutes(5))));
        }

        public Task<Result<DownloadChunk, DaemonError>> ReadDownloadChunkAsync(
            DownloadChunkRequest request,
            CancellationToken cancellationToken) => Fail<DownloadChunk>();

        public Task<Result<Unit, DaemonError>> CloseDownloadAsync(
            Guid sessionId,
            CancellationToken cancellationToken)
        {
            CallCount++;
            ClosedDownloads.Add(sessionId);
            return Task.FromResult(Result.Ok<Unit, DaemonError>(default));
        }

        public Task<Result<Unit, DaemonError>> CreateDirectoryAsync(
            PathRequest request,
            CancellationToken cancellationToken) => Fail<Unit>();

        public Task<Result<Unit, DaemonError>> DeleteFileAsync(
            PathRequest request,
            CancellationToken cancellationToken) => Fail<Unit>();

        public Task<Result<Unit, DaemonError>> DeleteDirectoryAsync(
            DeleteDirectoryRequest request,
            CancellationToken cancellationToken) => Fail<Unit>();

        public Task<Result<Unit, DaemonError>> RenameFileAsync(
            PathRenameRequest request,
            CancellationToken cancellationToken) => Fail<Unit>();

        public Task<Result<Unit, DaemonError>> RenameDirectoryAsync(
            PathRenameRequest request,
            CancellationToken cancellationToken) => Fail<Unit>();

        public Task<Result<Unit, DaemonError>> MoveFileAsync(
            PathTransferRequest request,
            CancellationToken cancellationToken) => Fail<Unit>();

        public Task<Result<Unit, DaemonError>> MoveDirectoryAsync(
            PathTransferRequest request,
            CancellationToken cancellationToken) => Fail<Unit>();

        public Task<Result<Unit, DaemonError>> CopyFileAsync(
            PathTransferRequest request,
            CancellationToken cancellationToken) => Fail<Unit>();

        public Task<Result<Unit, DaemonError>> CopyDirectoryAsync(
            PathTransferRequest request,
            CancellationToken cancellationToken) => Fail<Unit>();

        public Task<Result<UploadSession, DaemonError>> OpenUploadAsync(
            UploadOpenRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(Result.Ok<UploadSession, DaemonError>(
                new UploadSession(Guid.NewGuid(), 1, DateTimeOffset.UtcNow.AddMinutes(5))));
        }

        public Task<Result<Unit, DaemonError>> WriteUploadChunkAsync(
            UploadChunkRequest request,
            CancellationToken cancellationToken) => Fail<Unit>();

        public Task<Result<Unit, DaemonError>> CloseUploadAsync(
            Guid sessionId,
            CancellationToken cancellationToken) => Fail<Unit>();

        public Task<Result<Unit, DaemonError>> CancelUploadAsync(
            Guid sessionId,
            CancellationToken cancellationToken)
        {
            CallCount++;
            CanceledUploads.Add(sessionId);
            return Task.FromResult(Result.Ok<Unit, DaemonError>(default));
        }

        private Task<Result<T, DaemonError>> Fail<T>() where T : notnull
        {
            CallCount++;
            return Task.FromResult(Result.Err<T, DaemonError>(Inner));
        }
    }
}
