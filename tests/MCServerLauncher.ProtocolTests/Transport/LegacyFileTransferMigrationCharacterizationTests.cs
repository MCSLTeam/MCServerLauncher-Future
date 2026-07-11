using System.Collections.Concurrent;
using System.Text;
using MCServerLauncher.Common.ProtoType.Action;
using MCServerLauncher.Daemon.Storage;
using MCServerLauncher.DaemonClient;

namespace MCServerLauncher.ProtocolTests;

public class LegacyFileTransferMigrationCharacterizationTests
{
    private const string Category = "LegacyFileTransferMigration";

    [Fact]
    [Trait("Category", Category)]
    public async Task LegacyFileDownloadMigration_OpenReadEvenLengthClose_PreservesBytesAndCleansSession()
    {
        var expected = new byte[]
        {
            0x00, 0x41,
            0x00, 0x42,
            0x03, 0xA9,
            0x4E, 0x2D
        };
        var relativePath = $"caches/downloads/legacy-download-migration-{Guid.NewGuid():N}.bin";
        var resolvedPath = FileManager.ResolveAndValidatePath(relativePath);
        FileSessionCoordinator.LegacyDownloadSession? opened = null;

        Directory.CreateDirectory(Path.GetDirectoryName(resolvedPath)!);
        await File.WriteAllBytesAsync(resolvedPath, expected);

        try
        {
            var openedResult = await FileSessionCoordinator.Shared.OpenLegacyDownloadAsync(relativePath, CancellationToken.None);
            Assert.True(openedResult.IsOk(out var openedSession));
            opened = openedSession;

            var session = opened.Value;
            Assert.Equal(expected.Length, session.Size);
            Assert.Equal(await FileManager.FileSha1(resolvedPath), session.Sha1);

            var chunk = await FileSessionCoordinator.Shared.ReadLegacyDownloadRangeAsync(
                session.SessionId,
                0,
                expected.Length,
                CancellationToken.None);
            Assert.True(chunk.IsOk(out var bytes));
            Assert.Equal(expected, bytes);

            await FileSessionCoordinator.Shared.CloseLegacyDownloadAsync(session.SessionId, CancellationToken.None);
            opened = null;

            var closeAgain = await FileSessionCoordinator.Shared.CloseLegacyDownloadAsync(session.SessionId, CancellationToken.None);
            Assert.True(closeAgain.IsErr(out _));

            File.Delete(resolvedPath);
            Assert.False(File.Exists(resolvedPath));
        }
        finally
        {
            if (opened is { } session)
            {
                await FileSessionCoordinator.Shared.CloseLegacyDownloadAsync(session.SessionId, CancellationToken.None);
            }

            DeleteIfExists(resolvedPath);
        }
    }

    [Fact]
    [Trait("Category", Category)]
    public async Task LegacyFileDownloadMigration_InvalidRangeSessionAndClose_FailWithoutLeakingOpenSession()
    {
        var expected = new byte[] { 0x00, 0x41, 0x00, 0x42 };
        var relativePath = $"caches/downloads/legacy-download-invalid-migration-{Guid.NewGuid():N}.bin";
        var resolvedPath = FileManager.ResolveAndValidatePath(relativePath);
        FileSessionCoordinator.LegacyDownloadSession? opened = null;

        Directory.CreateDirectory(Path.GetDirectoryName(resolvedPath)!);
        await File.WriteAllBytesAsync(resolvedPath, expected);

        try
        {
            var openedResult = await FileSessionCoordinator.Shared.OpenLegacyDownloadAsync(relativePath, CancellationToken.None);
            Assert.True(openedResult.IsOk(out var openedSession));
            opened = openedSession;
            var session = opened.Value;

            var invalidOffset = await FileSessionCoordinator.Shared.ReadLegacyDownloadRangeAsync(session.SessionId, -1, 2, CancellationToken.None);
            Assert.True(invalidOffset.IsErr(out _));
            var invalidRange = await FileSessionCoordinator.Shared.ReadLegacyDownloadRangeAsync(session.SessionId, 3, 2, CancellationToken.None);
            Assert.True(invalidRange.IsErr(out _));
            var missingSession = await FileSessionCoordinator.Shared.ReadLegacyDownloadRangeAsync(Guid.NewGuid(), 0, 2, CancellationToken.None);
            Assert.True(missingSession.IsErr(out _));
            var missingClose = await FileSessionCoordinator.Shared.CloseLegacyDownloadAsync(Guid.NewGuid(), CancellationToken.None);
            Assert.True(missingClose.IsErr(out _));

            var validRange = await FileSessionCoordinator.Shared.ReadLegacyDownloadRangeAsync(
                session.SessionId,
                0,
                expected.Length,
                CancellationToken.None);
            Assert.True(validRange.IsOk(out var bytes));
            Assert.Equal(expected, bytes);

            await FileSessionCoordinator.Shared.CloseLegacyDownloadAsync(session.SessionId, CancellationToken.None);
            opened = null;

            File.Delete(resolvedPath);
            Assert.False(File.Exists(resolvedPath));
        }
        finally
        {
            if (opened is { } session)
            {
                await FileSessionCoordinator.Shared.CloseLegacyDownloadAsync(session.SessionId, CancellationToken.None);
            }

            DeleteIfExists(resolvedPath);
        }
    }

    [Fact]
    [Trait("Category", Category)]
    public async Task LegacyDaemonClientDownloadMigration_PublicExtension_ReportsProgressDoneAndClosesSession()
    {
        var fileId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var expected = new byte[]
        {
            0x00, 0x41,
            0x00, 0x42,
            0x00, 0x43,
            0x00, 0x44
        };
        var destination = Path.Combine(Path.GetTempPath(), $"mcsl-legacy-download-{Guid.NewGuid():N}.bin");
        const int timeout = 4321;
        const int chunkTimeout = 5678;
        using var cancellationSource = new CancellationTokenSource();

        using var daemon = new RecordingDaemon(call =>
        {
            return call.ActionType switch
            {
                ActionType.FileDownloadRequest => Task.FromResult<object?>(new FileDownloadRequestResult
                {
                    FileId = fileId,
                    Size = expected.Length,
                    Sha1 = "migration-sha1"
                }),
                ActionType.FileDownloadRange => Task.FromResult<object?>(CreateRangeResult(call.Parameter, expected)),
                ActionType.FileDownloadClose => Task.FromResult<object?>(null),
                _ => throw new InvalidOperationException($"Unexpected legacy download action: {call.ActionType}")
            };
        });

        try
        {
            var context = await daemon.DownloadFileAsync(
                "/instances/migration/file.bin",
                destination,
                chunkSize: 4,
                timeout,
                chunkTimeout,
                cancellationSource.Token);

            Assert.NotNull(context.NetworkLoadTask);
            await context.NetworkLoadTask.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.True(context.Done);
            Assert.Equal(expected.Length, context.LoadedBytes);
            Assert.Equal(NetworkLoadContextState.Closed, context.State);
            Assert.Equal(expected, await File.ReadAllBytesAsync(destination));

            var calls = daemon.Calls.ToArray();
            Assert.Collection(
                calls,
                open =>
                {
                    Assert.Equal(ActionType.FileDownloadRequest, open.ActionType);
                    var parameter = Assert.IsType<FileDownloadRequestParameter>(open.Parameter);
                    Assert.Equal("/instances/migration/file.bin", parameter.Path);
                    Assert.Equal(chunkTimeout, parameter.Timeout);
                },
                firstRange => AssertLegacyRange(firstRange, fileId, "0..4"),
                secondRange => AssertLegacyRange(secondRange, fileId, "4..8"),
                close =>
                {
                    Assert.Equal(ActionType.FileDownloadClose, close.ActionType);
                    Assert.Equal(fileId, Assert.IsType<FileDownloadCloseParameter>(close.Parameter).FileId);
                });

            Assert.All(calls, call =>
            {
                Assert.Equal(timeout, call.Timeout);
                Assert.Equal(cancellationSource.Token, call.CancellationToken);
            });
        }
        finally
        {
            DeleteIfExists(destination);
        }
    }

    [Fact]
    [Trait("Category", Category)]
    public async Task LegacyDaemonClientUploadMigration_PublicContextCancel_PropagatesUploadCancel()
    {
        var fileId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        using var cancellationSource = new CancellationTokenSource();
        using var operationSource = new CancellationTokenSource();
        using var daemon = new RecordingDaemon(_ => Task.FromResult<object?>(null));
        var context = new UploadContext(
            fileId,
            operationSource,
            new NetworkLoadSpeed { TotalBytes = 16 },
            daemon)
        {
            NetworkLoadTask = Task.CompletedTask
        };

        await context.CancelAsync(2000, cancellationSource.Token);

        Assert.True(operationSource.IsCancellationRequested);
        Assert.Equal(NetworkLoadContextState.Closed, context.State);
        var call = Assert.Single(daemon.Calls);
        Assert.Equal(ActionType.FileUploadCancel, call.ActionType);
        Assert.Equal(fileId, Assert.IsType<FileUploadCancelParameter>(call.Parameter).FileId);
        Assert.Equal(cancellationSource.Token, call.CancellationToken);
    }

    [Fact]
    [Trait("Category", Category)]
    public async Task LegacyDaemonClientDownloadMigration_PublicContextCancel_PropagatesDownloadClose()
    {
        var fileId = Guid.Parse("99999999-8888-7777-6666-555555555555");
        using var cancellationSource = new CancellationTokenSource();
        using var operationSource = new CancellationTokenSource();
        using var daemon = new RecordingDaemon(_ => Task.FromResult<object?>(null));
        var context = new DownloadContext(
            fileId,
            operationSource,
            new NetworkLoadSpeed { TotalBytes = 16 },
            daemon)
        {
            NetworkLoadTask = Task.CompletedTask
        };

        await context.CancelAsync(2000, cancellationSource.Token);

        Assert.True(operationSource.IsCancellationRequested);
        Assert.Equal(NetworkLoadContextState.Closed, context.State);
        var call = Assert.Single(daemon.Calls);
        Assert.Equal(ActionType.FileDownloadClose, call.ActionType);
        Assert.Equal(fileId, Assert.IsType<FileDownloadCloseParameter>(call.Parameter).FileId);
        Assert.Equal(cancellationSource.Token, call.CancellationToken);
    }

    private static FileDownloadRangeResult CreateRangeResult(IActionParameter? parameter, byte[] source)
    {
        var range = Assert.IsType<FileDownloadRangeParameter>(parameter);
        var bounds = range.Range.Split("..", StringSplitOptions.None);
        Assert.Equal(2, bounds.Length);
        var from = int.Parse(bounds[0]);
        var to = int.Parse(bounds[1]);

        return new FileDownloadRangeResult
        {
            Content = Encoding.BigEndianUnicode.GetString(source, from, to - from)
        };
    }

    private static void AssertLegacyRange(LegacyRequestCall call, Guid fileId, string range)
    {
        Assert.Equal(ActionType.FileDownloadRange, call.ActionType);
        var parameter = Assert.IsType<FileDownloadRangeParameter>(call.Parameter);
        Assert.Equal(fileId, parameter.FileId);
        Assert.Equal(range, parameter.Range);
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    private readonly record struct LegacyRequestCall(
        ActionType ActionType,
        IActionParameter? Parameter,
        int Timeout,
        CancellationToken CancellationToken,
        Type? ResultType);

    private sealed class RecordingDaemon : IDaemon
    {
        private readonly Func<LegacyRequestCall, Task<object?>> _handler;

        public RecordingDaemon(Func<LegacyRequestCall, Task<object?>> handler)
        {
            _handler = handler;
        }

        public ConcurrentQueue<LegacyRequestCall> Calls { get; } = new();
        public bool Online => true;
        public bool IsConnectionLost => false;
        public DateTime LastPong => DateTime.UtcNow;
        public SubscribedEvents SubscribedEvents { get; } = new();

        event Action? IDaemon.Reconnected
        {
            add { }
            remove { }
        }

        event Action? IDaemon.ConnectionLost
        {
            add { }
            remove { }
        }

        event Action? IDaemon.ConnectionClosed
        {
            add { }
            remove { }
        }

        event Action<Guid, string>? IDaemon.InstanceLogEvent
        {
            add { }
            remove { }
        }

        event DaemonInstanceLogEventHandler? IDaemon.InstanceLogEventAsync
        {
            add { }
            remove { }
        }

        event Action<MCServerLauncher.Common.ProtoType.Status.DaemonReport, long>? IDaemon.DaemonReportEvent
        {
            add { }
            remove { }
        }

        event DaemonReportEventHandler? IDaemon.DaemonReportEventAsync
        {
            add { }
            remove { }
        }

        public async Task RequestAsync(
            ActionType actionType,
            IActionParameter? parameter,
            int timeout = -1,
            CancellationToken cancellationToken = default)
        {
            var call = new LegacyRequestCall(actionType, parameter, timeout, cancellationToken, null);
            Calls.Enqueue(call);
            await _handler(call);
        }

        public async Task<TResult> RequestAsync<TResult>(
            ActionType actionType,
            IActionParameter? parameter,
            int timeout = -1,
            CancellationToken cancellationToken = default)
            where TResult : class, IActionResult
        {
            var call = new LegacyRequestCall(actionType, parameter, timeout, cancellationToken, typeof(TResult));
            Calls.Enqueue(call);
            var result = await _handler(call);
            return result as TResult
                   ?? throw new InvalidOperationException(
                       $"Legacy fake returned {result?.GetType().FullName ?? "null"} for {typeof(TResult).FullName}.");
        }

        public Task CloseAsync()
        {
            return Task.CompletedTask;
        }

        public void Dispose()
        {
        }
    }
}
