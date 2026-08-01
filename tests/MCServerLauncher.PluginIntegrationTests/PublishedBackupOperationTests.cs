using System.Collections.Immutable;
using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using MCServerLauncher.Common.Contracts.Backup;
using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Common.Contracts.Operations;
using MCServerLauncher.Common.Contracts.Provisioning;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.DaemonClient;
using Xunit.Abstractions;

namespace MCServerLauncher.PluginIntegrationTests;

/// <summary>
/// Drives the built-in backup RPCs against a published daemon, including a real long-running
/// operation that is polled while non-terminal, cancelled mid-flight, and then re-run to success.
/// </summary>
public sealed class PublishedBackupOperationTests(ITestOutputHelper output)
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PhaseTimeout = TimeSpan.FromSeconds(120);
    // Kept well under the restore's pre-swap duration: the poll interval is the dominant term in
    // how late a cancel can arrive.
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    // Sizes the cancel window: a restore is only cancellable before its staging directory is
    // swapped in, so extraction has to take meaningfully longer than one poll round trip. Deflate
    // cannot shrink random bytes, so the seeded size is also the archived and extracted size.
    private const int SeedFileCount = 8;
    private const int SeedFileBytes = 32 * 1024 * 1024;

    private const string MarkerFileName = "backup-marker.txt";
    private const string OriginalMarker = "marker-content-captured-by-the-archive";
    private const string MutatedMarker = "marker-content-written-after-the-archive";

    /// <summary>
    /// Every principal field is rebound daemon-side to the connection subject, so the value sent
    /// here is only a placeholder that keeps the request serializable.
    /// </summary>
    private const string RequestPrincipal = "published-host-e2e";

    private static readonly JsonElement EmptyEventRules = JsonDocument.Parse("[]").RootElement.Clone();

    [Fact]
    [Trait("Category", "PublishedPlugin")]
    public async Task PublishedDaemon_CreatesABackupThenCancelsAndCompletesARestore()
    {
        var publishedDaemon = Environment.GetEnvironmentVariable("MCSL_PUBLISHED_DAEMON");
        Assert.False(
            string.IsNullOrWhiteSpace(publishedDaemon),
            "MCSL_PUBLISHED_DAEMON must point to a published daemon executable or directory for published-plugin acceptance.");

        await using var fixture = await PublishedDaemonFixture.CreateAsync(publishedDaemon!, includePlugins: false);
        await fixture.StartAsync();

        await using var client = new global::MCServerLauncher.DaemonClient.DaemonClient(new DaemonClientOptions(
            fixture.EndpointUri,
            fixture.Token,
            RequestTimeout,
            TimeSpan.FromMilliseconds(100)));
        var connected = await client.ConnectAsync().WaitAsync(RequestTimeout);
        Assert.True(connected.IsOk(out _), connected.IsErr(out var connectionError) ? connectionError!.Message : null);

        var instanceId = await CreateStoppedInstanceAsync(client, fixture);
        var workingDirectory = Path.Combine(fixture.Root, "daemon", "instances", instanceId.ToString());
        var markerPath = Path.Combine(workingDirectory, MarkerFileName);
        await File.WriteAllTextAsync(markerPath, OriginalMarker);
        SeedIncompressibleFiles(workingDirectory);

        var create = await client.Backups
            .CreateAsync(new BackupCreateRequest(instanceId, Maintenance: false, RequestPrincipal), CancellationToken.None)
            .WaitAsync(RequestTimeout);
        Assert.True(create.IsOk(out var createAccepted), create.IsErr(out var createError) ? createError!.Message : null);

        var createObserved = new List<OperationSnapshot>();
        var createTerminal = await PollAsync(client, createAccepted!.OperationId, IsTerminal, PhaseTimeout, createObserved);
        Assert.Contains(
            createObserved,
            snapshot => !IsTerminal(snapshot) && !IsTerminalStage(snapshot.Stage));
        Assert.Equal(OperationStatus.Succeeded, createTerminal.Status);

        var list = await client.Backups
            .ListAsync(new BackupListQuery(instanceId), CancellationToken.None)
            .WaitAsync(RequestTimeout);
        Assert.True(list.IsOk(out var listed), list.IsErr(out var listError) ? listError!.Message : null);
        var archive = Assert.Single(listed!.Archives);
        Assert.Equal(instanceId, archive.InstanceId);
        Assert.Equal(archive.ArchiveId.ToString("D"), createTerminal.ResultReference);
        // Without the seeded payload in the archive the restore below finishes too fast to cancel,
        // and the cancel assertion would pass for the wrong reason.
        Assert.True(
            archive.TotalSizeBytes >= SeedFileCount * (long)SeedFileBytes,
            $"The archive captured only {archive.TotalSizeBytes} bytes of instance data.");

        await File.WriteAllTextAsync(markerPath, MutatedMarker);

        var cancelledRestore = await ExecuteRestoreAsync(client, archive.ArchiveId, instanceId);
        var cancelObserved = new List<OperationSnapshot>();
        await PollAsync(
            client,
            cancelledRestore.OperationId,
            snapshot => snapshot.Status is OperationStatus.Running,
            PhaseTimeout,
            cancelObserved);

        var requestedAt = Stopwatch.GetTimestamp();
        var cancel = await client.Operations
            .CancelOperationAsync(new OperationCancelRequest(cancelledRestore.OperationId), CancellationToken.None)
            .WaitAsync(RequestTimeout);
        Assert.True(cancel.IsOk(out var cancelResult), cancel.IsErr(out var cancelError) ? cancelError!.Message : null);
        Assert.True(cancelResult!.CancelRequested);

        var cancelTerminal = await PollAsync(client, cancelledRestore.OperationId, IsTerminal, PhaseTimeout, cancelObserved);
        // The margin between this and the restore's pre-swap work is what keeps the cancel
        // assertion below from racing; it is reported so a shrinking window is visible before
        // it starts flaking.
        output.WriteLine($"Cancel acknowledged to terminal in {Stopwatch.GetElapsedTime(requestedAt)}.");
        Assert.Equal(OperationStatus.Cancelled, cancelTerminal.Status);
        Assert.Equal(
            MutatedMarker,
            await File.ReadAllTextAsync(markerPath));

        var succeedingRestore = await ExecuteRestoreAsync(client, archive.ArchiveId, instanceId);
        var restoreObserved = new List<OperationSnapshot>();
        var restoreStartedAt = Stopwatch.GetTimestamp();
        var restoreTerminal = await PollAsync(client, succeedingRestore.OperationId, IsTerminal, PhaseTimeout, restoreObserved);
        output.WriteLine($"Uncancelled restore ran for {Stopwatch.GetElapsedTime(restoreStartedAt)}.");
        Assert.Equal(OperationStatus.Succeeded, restoreTerminal.Status);
        Assert.Equal(archive.ArchiveId.ToString("D"), restoreTerminal.ResultReference);
        Assert.Equal(
            OriginalMarker,
            await File.ReadAllTextAsync(markerPath));

        var logs = await fixture.StopAndReadLogsAsync();
        Assert.True(fixture.GracefulStopObserved, "Published daemon did not complete its console-driven graceful shutdown.");
        Assert.DoesNotContain("restore_manual_recovery_required", logs, StringComparison.Ordinal);
    }

    private static async Task<BackupRestoreExecuteResult> ExecuteRestoreAsync(
        global::MCServerLauncher.DaemonClient.DaemonClient client,
        Guid archiveId,
        Guid instanceId)
    {
        var planned = await client.Backups
            .PlanRestoreAsync(new BackupRestorePlanRequest(archiveId, instanceId, RequestPrincipal), CancellationToken.None)
            .WaitAsync(RequestTimeout);
        Assert.True(planned.IsOk(out var plan), planned.IsErr(out var planError) ? planError!.Message : null);
        Assert.Equal(PlanStatus.Blocked, plan!.Status);
        Assert.True(plan.RequiresConfirmation);

        var confirmed = await client.Backups
            .ConfirmRestoreAsync(
                new BackupRestoreConfirmRequest(plan.PlanId, plan.PlanHash, RequestPrincipal),
                CancellationToken.None)
            .WaitAsync(RequestTimeout);
        Assert.True(confirmed.IsOk(out var ready), confirmed.IsErr(out var confirmError) ? confirmError!.Message : null);
        Assert.Equal(PlanStatus.Ready, ready!.Status);

        var executed = await client.Backups
            .ExecuteRestoreAsync(new BackupRestoreExecuteRequest(ready.PlanId, RequestPrincipal), CancellationToken.None)
            .WaitAsync(RequestTimeout);
        Assert.True(executed.IsOk(out var execution), executed.IsErr(out var executeError) ? executeError!.Message : null);
        Assert.Equal(ready.PlanId, execution!.PlanId);
        return execution;
    }

    private static async Task<OperationSnapshot> PollAsync(
        global::MCServerLauncher.DaemonClient.DaemonClient client,
        Guid operationId,
        Func<OperationSnapshot, bool> until,
        TimeSpan deadline,
        List<OperationSnapshot> observed)
    {
        var startedAt = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(startedAt) < deadline)
        {
            var result = await client.Operations
                .GetOperationAsync(new OperationReference(operationId), CancellationToken.None)
                .WaitAsync(RequestTimeout);
            Assert.True(result.IsOk(out var snapshot), result.IsErr(out var error) ? error!.Message : null);
            observed.Add(snapshot!);
            if (until(snapshot!))
                return snapshot!;

            await Task.Delay(PollInterval);
        }

        var trail = string.Join(", ", observed.Select(static snapshot => $"{snapshot.Status}/{snapshot.Stage}"));
        throw new TimeoutException(
            $"Operation {operationId:D} did not reach the awaited state within {deadline}. Observed: {trail}.");
    }

    private static async Task<Guid> CreateStoppedInstanceAsync(
        global::MCServerLauncher.DaemonClient.DaemonClient client,
        PublishedDaemonFixture fixture)
    {
        // The universal factory copies this file into the working directory and the passthrough
        // installer leaves it alone, so a real but empty zip creates an instance without any
        // download, installer run, or Java process.
        var sourceDirectory = Path.Combine(fixture.Root, "daemon", "caches", "uploads");
        Directory.CreateDirectory(sourceDirectory);
        using (var core = ZipFile.Open(Path.Combine(sourceDirectory, "server.jar"), ZipArchiveMode.Create))
        {
            core.CreateEntry("META-INF/");
        }

        var setting = new InstanceFactoryConfiguration(
            new InstanceConfiguration(
                Guid.NewGuid(),
                "published-backup-e2e",
                "server.jar",
                InstanceType.MCJava,
                TargetType.Jar,
                string.Empty,
                "utf-8",
                "utf-8",
                "java",
                ImmutableArray<string>.Empty,
                ImmutableDictionary<string, string>.Empty,
                EmptyEventRules),
            // Daemon-root-relative: a source outside the daemon root is rejected by path validation.
            "caches/uploads/server.jar",
            SourceType.Core,
            InstanceFactoryMirror.None,
            UsePostProcess: false);

        var created = await client.Instances
            .CreateInstanceAsync(new CreateInstanceRequest(setting), CancellationToken.None)
            .WaitAsync(RequestTimeout);
        Assert.True(created.IsOk(out var result), created.IsErr(out var error) ? error!.Message : null);

        var instanceId = result!.Config.InstanceId;
        var startedAt = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(startedAt) < PhaseTimeout)
        {
            if (client.InstanceCatalog.TryGet(instanceId, out var snapshot) &&
                snapshot.Status == InstanceStatus.Stopped)
            {
                return instanceId;
            }

            await Task.Delay(PollInterval);
        }

        throw new TimeoutException(
            $"Instance {instanceId:D} did not appear as stopped in the mirrored catalog within {PhaseTimeout}.");
    }

    private static void SeedIncompressibleFiles(string workingDirectory)
    {
        var buffer = new byte[1024 * 1024];
        for (var file = 0; file < SeedFileCount; file++)
        {
            using var stream = new FileStream(
                Path.Combine(workingDirectory, $"payload-{file}.bin"),
                FileMode.Create,
                FileAccess.Write,
                FileShare.None);
            for (var written = 0; written < SeedFileBytes; written += buffer.Length)
            {
                Random.Shared.NextBytes(buffer);
                stream.Write(buffer);
            }
        }
    }

    private static bool IsTerminal(OperationSnapshot snapshot) =>
        snapshot.Status is OperationStatus.Succeeded
            or OperationStatus.Failed
            or OperationStatus.Cancelled
            or OperationStatus.Interrupted;

    private static bool IsTerminalStage(OperationStage stage) =>
        stage is OperationStage.Succeeded
            or OperationStage.Failed
            or OperationStage.Cancelled
            or OperationStage.Interrupted;
}
