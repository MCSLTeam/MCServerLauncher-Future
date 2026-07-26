using MCServerLauncher.Common.Contracts.Backup;
using MCServerLauncher.Common.Contracts.Instances;
using MCServerLauncher.Common.Contracts.Operations;
using MCServerLauncher.Common.Contracts.Provisioning;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.ApplicationCore.Backups;
using MCServerLauncher.Daemon.ApplicationCore.Operations;
using MCServerLauncher.Daemon.ApplicationCore.Provisioning;
using MCServerLauncher.Daemon.Management;
using MCServerLauncher.Daemon.Storage;
using RustyOptions;
using ContractInstanceReport = MCServerLauncher.Common.Contracts.Instances.InstanceReport;

namespace MCServerLauncher.ProtocolTests;

[Collection(Helpers.DaemonInstanceStorageIsolationCollection.Name)]
public sealed class LocalBackupApplicationTests
{
    [Theory]
    [InlineData(InstanceStatus.Running)]
    [InlineData(InstanceStatus.Starting)]
    [InlineData(InstanceStatus.Stopping)]
    public async Task Create_RejectsDirectBackupOfAnInstanceThatIsNotStopped(InstanceStatus status)
    {
        using var harness = await Harness.CreateAsync();
        harness.Instance.Status = status;

        var created = await harness.Application.CreateAsync(
            new BackupCreateRequest(harness.InstanceId, Maintenance: false, "owner-a"),
            CancellationToken.None);

        Assert.True(created.IsErr(out var error));
        Assert.Equal("instance.running", error!.Code);
        Assert.Empty(harness.Store.List());
    }

    [Theory]
    [InlineData(InstanceStatus.Stopped)]
    [InlineData(InstanceStatus.Crashed)]
    public async Task Create_ArchivesStoppedOrCrashedInstances(InstanceStatus status)
    {
        using var harness = await Harness.CreateAsync();
        harness.Instance.Status = status;

        var created = await harness.Application.CreateAsync(
            new BackupCreateRequest(harness.InstanceId, Maintenance: false, "owner-a"),
            CancellationToken.None);

        Assert.True(created.IsOk(out var result));
        var operation = await harness.WaitForTerminalAsync(result!.OperationId);
        Assert.Equal(OperationStatus.Succeeded, operation.Status);
        var manifest = Assert.Single(harness.Store.List());
        Assert.Equal(manifest.ArchiveId.ToString("D"), operation.ResultReference);
        Assert.Contains(manifest.Files, file => file.RelativePath == "server.jar");
    }

    [Fact]
    public async Task Create_MaintenanceStopsAndRestartsAndArchivesOnlyAfterRealExit()
    {
        using var harness = await Harness.CreateAsync();
        harness.Instance.Status = InstanceStatus.Running;
        // Stop commits Stopping only; the archive must not run until the instance is truly stopped.
        harness.Instances.OnStop = () => harness.Instance.Status = InstanceStatus.Stopped;
        harness.Instances.OnStart = () => harness.Instance.Status = InstanceStatus.Running;

        var created = await harness.Application.CreateAsync(
            new BackupCreateRequest(harness.InstanceId, Maintenance: true, "owner-a"),
            CancellationToken.None);

        Assert.True(created.IsOk(out var result));
        var operation = await harness.WaitForTerminalAsync(result!.OperationId);
        Assert.Equal(OperationStatus.Succeeded, operation.Status);
        Assert.Equal(1, harness.Instances.StopCount);
        Assert.Equal(1, harness.Instances.StartCount);
        Assert.Single(harness.Store.List());
        Assert.Equal(InstanceStatus.Running, harness.Instance.Status);
    }

    [Fact]
    public async Task Create_MaintenanceFailsWhenTheInstanceNeverStops()
    {
        using var harness = await Harness.CreateAsync();
        harness.Instance.Status = InstanceStatus.Running;
        harness.Instances.OnStop = () => harness.Instance.Status = InstanceStatus.Stopping;

        var created = await harness.Application.CreateAsync(
            new BackupCreateRequest(harness.InstanceId, Maintenance: true, "owner-a"),
            CancellationToken.None);

        Assert.True(created.IsOk(out var result));
        var operation = await harness.WaitForTerminalAsync(result!.OperationId);
        Assert.Equal(OperationStatus.Failed, operation.Status);
        Assert.Equal("backup.stop_timeout", operation.ErrorCode);
        Assert.Empty(harness.Store.List());
        Assert.Equal(0, harness.Instances.StartCount);
    }

    [Fact]
    public async Task PlanRestore_IsBlockedUntilConfirmedAndBindsTheArchiveContent()
    {
        using var harness = await Harness.CreateAsync();
        var manifest = await harness.ArchiveAsync();

        var planned = await harness.Application.PlanRestoreAsync(
            new BackupRestorePlanRequest(manifest.ArchiveId, harness.InstanceId, "owner-a"),
            CancellationToken.None);
        Assert.True(planned.IsOk(out var plan));
        Assert.Equal(PlanStatus.Blocked, plan!.Status);
        Assert.Equal(PlanRiskClass.Destructive, plan.RiskClass);
        Assert.True(plan.RequiresConfirmation);
        Assert.Equal(manifest.Sha256, plan.Payload.GetProperty("archive_sha256").GetString());

        var premature = await harness.Application.ExecuteRestoreAsync(
            new BackupRestoreExecuteRequest(plan.PlanId, "owner-a"),
            CancellationToken.None);
        Assert.True(premature.IsErr(out var prematureError));
        Assert.Equal("plan.not_ready", prematureError!.Code);

        var confirmed = await harness.Application.ConfirmRestoreAsync(
            new BackupRestoreConfirmRequest(plan.PlanId, plan.PlanHash, "owner-a"),
            CancellationToken.None);
        Assert.True(confirmed.IsOk(out var confirmedPlan));
        Assert.Equal(PlanStatus.Ready, confirmedPlan!.Status);
        Assert.Equal("owner-a", confirmedPlan.ConfirmedBy);
    }

    [Fact]
    public async Task PlanRestore_RejectsAnArchiveFromAnotherInstance()
    {
        using var harness = await Harness.CreateAsync();
        var manifest = await harness.ArchiveAsync();

        var planned = await harness.Application.PlanRestoreAsync(
            new BackupRestorePlanRequest(manifest.ArchiveId, Guid.NewGuid(), "owner-a"),
            CancellationToken.None);

        Assert.True(planned.IsErr(out var error));
        Assert.Equal("backup.instance_mismatch", error!.Code);
    }

    [Fact]
    public async Task ExecuteRestore_ReplacesTheWorkingDirectoryAndConsumesThePlan()
    {
        using var harness = await Harness.CreateAsync();
        var manifest = await harness.ArchiveAsync();
        await File.WriteAllTextAsync(Path.Combine(harness.WorkingDirectory, "server.jar"), "mutated");
        await File.WriteAllTextAsync(Path.Combine(harness.WorkingDirectory, "stray.txt"), "added-after-backup");

        var plan = await harness.ConfirmedRestorePlanAsync(manifest);
        var executed = await harness.Application.ExecuteRestoreAsync(
            new BackupRestoreExecuteRequest(plan.PlanId, "owner-a"),
            CancellationToken.None);
        Assert.True(executed.IsOk(out var result));

        var operation = await harness.WaitForTerminalAsync(result!.OperationId);
        Assert.Equal(OperationStatus.Succeeded, operation.Status);
        Assert.Equal("core-bytes", await File.ReadAllTextAsync(Path.Combine(harness.WorkingDirectory, "server.jar")));
        Assert.False(File.Exists(Path.Combine(harness.WorkingDirectory, "stray.txt")));
        Assert.Empty(Directory.EnumerateDirectories(FileManager.InstancesRoot, ".restore-*"));

        var consumed = harness.Plans.Get(plan.PlanId);
        Assert.True(consumed.IsOk(out var consumedPlan));
        Assert.Equal(PlanStatus.Consumed, consumedPlan!.Status);
        var replay = await harness.Application.ExecuteRestoreAsync(
            new BackupRestoreExecuteRequest(plan.PlanId, "owner-a"),
            CancellationToken.None);
        Assert.True(replay.IsErr(out var replayError));
        Assert.Equal("plan.single_flight", replayError!.Code);
    }

    [Fact]
    public async Task ExecuteRestore_TamperedArchiveFailsTheOperationAndStillConsumesTheAcceptedPlan()
    {
        using var harness = await Harness.CreateAsync();
        var manifest = await harness.ArchiveAsync();
        var plan = await harness.ConfirmedRestorePlanAsync(manifest);
        await File.AppendAllTextAsync(harness.Store.GetArchivePath(manifest.ArchiveId), "tamper");

        var executed = await harness.Application.ExecuteRestoreAsync(
            new BackupRestoreExecuteRequest(plan.PlanId, "owner-a"),
            CancellationToken.None);
        Assert.True(executed.IsOk(out var result));

        var operation = await harness.WaitForTerminalAsync(result!.OperationId);
        Assert.Equal(OperationStatus.Failed, operation.Status);
        Assert.Equal("backup.checksum_mismatch", operation.ErrorCode);
        Assert.Equal("core-bytes", await File.ReadAllTextAsync(Path.Combine(harness.WorkingDirectory, "server.jar")));
        // The admission was accepted, so the single-use plan is spent even though the work failed.
        var consumed = harness.Plans.Get(plan.PlanId);
        Assert.True(consumed.IsOk(out var consumedPlan));
        Assert.Equal(PlanStatus.Consumed, consumedPlan!.Status);
    }

    [Fact]
    public async Task ExecuteRestore_RejectsAnInstanceThatIsNotStopped()
    {
        using var harness = await Harness.CreateAsync();
        var manifest = await harness.ArchiveAsync();
        var plan = await harness.ConfirmedRestorePlanAsync(manifest);
        harness.Instance.Status = InstanceStatus.Running;

        var executed = await harness.Application.ExecuteRestoreAsync(
            new BackupRestoreExecuteRequest(plan.PlanId, "owner-a"),
            CancellationToken.None);
        Assert.True(executed.IsOk(out var result));

        var operation = await harness.WaitForTerminalAsync(result!.OperationId);
        Assert.Equal(OperationStatus.Failed, operation.Status);
        Assert.Equal("instance.running", operation.ErrorCode);
    }

    [Fact]
    public async Task Prune_KeepsArchivesReferencedByAnActiveRestorePlan()
    {
        using var harness = await Harness.CreateAsync();
        var pinnedManifest = await harness.ArchiveAsync();
        _ = await harness.ConfirmedRestorePlanAsync(pinnedManifest);
        var unpinnedManifest = await harness.ArchiveAsync();

        // Prune through a restrictive-cap view of the same store: creating under those caps would
        // have retained nothing, and the point here is which archive survives, not when caps run.
        var restrictive = harness.WithRetention(
            new DaemonBackupConfig { RetentionDays = 365, MaximumCount = 1, MaximumBytes = 1 });
        var pruned = await restrictive.PruneAsync(new BackupPruneRequest("owner-a"), CancellationToken.None);

        Assert.True(pruned.IsOk(out var result));
        Assert.Equal(new[] { unpinnedManifest.ArchiveId }, result!.RemovedArchiveIds);
        Assert.True(harness.Store.Get(pinnedManifest.ArchiveId).IsOk(out _));
        Assert.True(File.Exists(harness.Store.GetArchivePath(pinnedManifest.ArchiveId)));
    }

    private sealed class Harness : IDisposable
    {
        private readonly string _root;

        private readonly string _backupsRoot;

        private Harness(
            string root,
            string backupsRoot,
            Guid instanceId,
            string workingDirectory,
            TestInstance instance,
            InstanceManager manager,
            StubInstanceApplication instances,
            OperationCoordinator operations,
            PlanKernel plans,
            BackupArchiveStore store,
            LocalBackupApplication application)
        {
            _root = root;
            _backupsRoot = backupsRoot;
            InstanceId = instanceId;
            WorkingDirectory = workingDirectory;
            Instance = instance;
            Manager = manager;
            Instances = instances;
            Operations = operations;
            Plans = plans;
            Store = store;
            Application = application;
        }

        internal Guid InstanceId { get; }
        internal string WorkingDirectory { get; }
        internal TestInstance Instance { get; }
        internal InstanceManager Manager { get; }
        internal StubInstanceApplication Instances { get; }
        internal OperationCoordinator Operations { get; }
        internal PlanKernel Plans { get; }
        internal BackupArchiveStore Store { get; }
        internal LocalBackupApplication Application { get; }

        /// <summary>
        /// A second application over the same archive root and plan kernel, with different retention
        /// caps. Retention runs after every create, so building the archives under strict caps would
        /// delete them before the test can assert which one survives.
        /// </summary>
        internal LocalBackupApplication WithRetention(DaemonBackupConfig config) =>
            new(
                new BackupArchiveStore(config, rootDirectory: _backupsRoot),
                Plans,
                Manager,
                Instances,
                Operations);

        internal static async Task<Harness> CreateAsync(DaemonBackupConfig? config = null)
        {
            var root = Directory.CreateTempSubdirectory("mcsl-backup-app-").FullName;
            var instanceId = Guid.NewGuid();
            var instanceConfig = new InstanceConfig
            {
                Name = "demo",
                Target = "server.jar",
                InstanceType = InstanceType.Universal,
                TargetType = TargetType.Jar,
                Version = "1.21",
                Uuid = instanceId
            };
            var workingDirectory = instanceConfig.GetWorkingDirectory();
            Directory.CreateDirectory(workingDirectory);
            await File.WriteAllTextAsync(Path.Combine(workingDirectory, "server.jar"), "core-bytes");

            var instance = new TestInstance(instanceConfig);
            var manager = (InstanceManager)InstanceManager.Create();
            manager.ReplaceInstance(instanceId, instance);
            var operations = new OperationCoordinator(rootDirectory: Path.Combine(root, "ops"));
            var plans = new PlanKernel(rootDirectory: Path.Combine(root, "plans"));
            var backupsRoot = Path.Combine(root, "backups");
            var store = new BackupArchiveStore(config ?? new DaemonBackupConfig(), rootDirectory: backupsRoot);
            var instances = new StubInstanceApplication();
            var application = new LocalBackupApplication(store, plans, manager, instances, operations);
            return new Harness(
                root, backupsRoot, instanceId, workingDirectory, instance, manager, instances, operations, plans, store, application);
        }

        internal async Task<BackupArchiveManifest> ArchiveAsync()
        {
            Instance.Status = InstanceStatus.Stopped;
            var created = await Application.CreateAsync(
                new BackupCreateRequest(InstanceId, Maintenance: false, "owner-a"),
                CancellationToken.None);
            Assert.True(created.IsOk(out var result));
            var operation = await WaitForTerminalAsync(result!.OperationId);
            Assert.Equal(OperationStatus.Succeeded, operation.Status);
            var archiveId = Guid.Parse(operation.ResultReference!);
            Assert.True(Store.Get(archiveId).IsOk(out var manifest));
            return manifest!;
        }

        internal async Task<ProvisioningPlanSnapshot> ConfirmedRestorePlanAsync(BackupArchiveManifest manifest)
        {
            var planned = await Application.PlanRestoreAsync(
                new BackupRestorePlanRequest(manifest.ArchiveId, InstanceId, "owner-a"),
                CancellationToken.None);
            Assert.True(planned.IsOk(out var plan));
            var confirmed = await Application.ConfirmRestoreAsync(
                new BackupRestoreConfirmRequest(plan!.PlanId, plan.PlanHash, "owner-a"),
                CancellationToken.None);
            Assert.True(confirmed.IsOk(out var confirmedPlan));
            return confirmedPlan!;
        }

        internal async Task<OperationSnapshot> WaitForTerminalAsync(Guid operationId)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            while (true)
            {
                var current = await Operations.GetOperationAsync(
                    new OperationReference(operationId, "owner-a"),
                    CancellationToken.None);
                Assert.True(current.IsOk(out var snapshot));
                if (snapshot!.Status is OperationStatus.Succeeded
                    or OperationStatus.Failed
                    or OperationStatus.Cancelled
                    or OperationStatus.Interrupted)
                {
                    return snapshot;
                }

                await Task.Delay(10, timeout.Token);
            }
        }

        public void Dispose()
        {
            Operations.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Instance.Dispose();
            if (Directory.Exists(WorkingDirectory))
                Directory.Delete(WorkingDirectory, recursive: true);
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class TestInstance(InstanceConfig config) : IInstance
    {
        public InstanceConfig Config { get; } = config;
        public MCServerLauncher.Daemon.Management.Communicate.InstanceProcess? Process => null;
        public InstanceStatus Status { get; set; } = InstanceStatus.Stopped;
        public int ServerProcessId => -1;

        public event Func<Guid, string, CancellationToken, Task>? OnLog
        {
            add { }
            remove { }
        }

        public event Func<Guid, InstanceStatus, CancellationToken, Task>? OnStatusChanged
        {
            add { }
            remove { }
        }

        public Task<MCServerLauncher.Common.ProtoType.Instance.InstanceReport> GetReportAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<bool> StartAsync(int delayToCheck = 500, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<bool> StopAsync(CancellationToken ct = default) => throw new NotSupportedException();

        public Task ForceKillAndClearAsync(CancellationToken ct = default) => Task.CompletedTask;

        public IReadOnlyList<string> GetLogHistory() => [];

        public void Dispose()
        {
        }
    }

    private sealed class StubInstanceApplication : MCServerLauncher.Daemon.API.Application.IInstanceApplication
    {
        internal Action? OnStop { get; set; }
        internal Action? OnStart { get; set; }
        internal int StopCount { get; private set; }
        internal int StartCount { get; private set; }

        public Task<Result<Unit, DaemonError>> StopInstanceAsync(InstanceReference request, CancellationToken cancellationToken)
        {
            StopCount++;
            OnStop?.Invoke();
            return Task.FromResult(Result.Ok<Unit, DaemonError>(Unit.Default));
        }

        public Task<Result<Unit, DaemonError>> StartInstanceAsync(InstanceReference request, CancellationToken cancellationToken)
        {
            StartCount++;
            OnStart?.Invoke();
            return Task.FromResult(Result.Ok<Unit, DaemonError>(Unit.Default));
        }

        public Task<Result<CreateInstanceResult, DaemonError>> CreateInstanceAsync(CreateInstanceRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> RemoveInstanceAsync(InstanceReference request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> HaltInstanceAsync(InstanceReference request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> SendCommandAsync(InstanceCommandRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<ConsoleSession, DaemonError>> OpenConsoleAsync(ConsoleOpenRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> ResizeConsoleAsync(ConsoleResizeRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> CloseConsoleAsync(ConsoleSessionReference request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<Unit, DaemonError>> WriteConsoleAsync(Guid sessionId, ReadOnlyMemory<byte> data, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<ContractInstanceReport, DaemonError>> GetInstanceReportAsync(InstanceReference request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<InstanceReportList, DaemonError>> ListInstanceReportsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<InstanceLogResult, DaemonError>> GetInstanceLogAsync(InstanceLogQuery request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<InstanceSettingsResult, DaemonError>> GetInstanceSettingsAsync(InstanceReference request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<UpdateInstanceSettingsResult, DaemonError>> UpdateInstanceSettingsAsync(UpdateInstanceSettingsRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
