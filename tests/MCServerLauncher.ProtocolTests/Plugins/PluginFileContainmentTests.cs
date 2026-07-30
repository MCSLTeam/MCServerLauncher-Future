using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using MCServerLauncher.Common.Contracts.Files;
using MCServerLauncher.Common.ProtoType.Instance;
using MCServerLauncher.Daemon.API.Application;
using MCServerLauncher.Daemon.API.Errors;
using MCServerLauncher.Daemon.API.Plugins;
using MCServerLauncher.Daemon.API.State;
using MCServerLauncher.Daemon.ApplicationCore.Auth;
using MCServerLauncher.Daemon.Management;
using MCServerLauncher.Daemon.Plugins;
using RustyOptions;

namespace MCServerLauncher.ProtocolTests;

/// <summary>
/// The daemon data root holds the audit, plan, automation, backup, operation and monitoring stores
/// beside instance data, so a plugin granted the file features must not be able to reach them
/// through the shared file application. Inside the instances root the instance directory itself and
/// the daemon's own persisted instance files are lifecycle state the instance manager owns, and a
/// plugin must not hold unbounded, foreign, or unrevokable file sessions.
/// </summary>
public sealed class PluginFileContainmentTests
{
    private static readonly Guid CatalogedInstance = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid AbsentInstance = Guid.Parse("00000000-0000-0000-0000-0000000000ff");
    private const string InstanceRoot = "instances/00000000-0000-0000-0000-000000000001";
    private const string InsideInstance = InstanceRoot + "/mods/x.jar";

    [Theory]
    [InlineData("audit")]
    [InlineData("plans")]
    [InlineData("automation")]
    [InlineData("backups")]
    [InlineData("operations")]
    [InlineData("monitoring")]
    [InlineData("instances/../audit")]
    [InlineData("/audit")]
    [InlineData("cores")]
    public async Task DaemonControlDirectoriesAreOutOfReach(string path)
    {
        var inner = new RecordingFileApplication();
        var contained = Create(inner);

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
    [InlineData("instances/")]
    public async Task RootsThemselvesCannotBeDeleted(string path)
    {
        var inner = new RecordingFileApplication();
        var contained = Create(inner);

        var result = await contained.DeleteDirectoryAsync(
            new DeleteDirectoryRequest(path, Recursive: true),
            CancellationToken.None);

        Assert.True(result.IsErr(out var error));
        Assert.Equal("plugin.file.out_of_containment", error!.Code);
        Assert.Equal(0, inner.CallCount);
    }

    /// <summary>
    /// The instances root is readable per-instance, never as a whole: a plugin that could list it
    /// would enumerate instances the catalog never showed it.
    /// </summary>
    [Theory]
    [InlineData("instances")]
    [InlineData("instances/")]
    public async Task TheInstancesRootIsNotEvenReadable(string path)
    {
        var inner = new RecordingFileApplication();
        var contained = Create(inner);

        var result = await contained.GetDirectoryInfoAsync(new PathRequest(path), CancellationToken.None);

        Assert.True(result.IsErr(out var error));
        Assert.Equal("plugin.file.out_of_containment", error!.Code);
        Assert.Equal(0, inner.CallCount);
    }

    /// <summary>
    /// Removing an instance checks running state, holds a mutation lease, updates the catalog and
    /// the published snapshot, then stages the rename and the delete. Every one of those invariants
    /// is bypassed if the file facade lets a plugin mutate the instance directory itself.
    /// </summary>
    [Fact]
    public async Task TheInstanceDirectoryItselfCannotBeMutated()
    {
        var inner = new RecordingFileApplication();
        var contained = Create(inner);

        var results = new List<Result<Unit, DaemonError>>
        {
            await contained.CreateDirectoryAsync(new PathRequest(InstanceRoot), CancellationToken.None),
            await contained.DeleteFileAsync(new PathRequest(InstanceRoot), CancellationToken.None),
            await contained.DeleteDirectoryAsync(
                new DeleteDirectoryRequest(InstanceRoot, Recursive: true),
                CancellationToken.None),
            await contained.RenameFileAsync(
                new PathRenameRequest(InstanceRoot, "seized"),
                CancellationToken.None),
            await contained.RenameDirectoryAsync(
                new PathRenameRequest(InstanceRoot, "seized"),
                CancellationToken.None),
            await contained.MoveFileAsync(
                new PathTransferRequest(InstanceRoot, InsideInstance),
                CancellationToken.None),
            await contained.MoveDirectoryAsync(
                new PathTransferRequest(InstanceRoot, InsideInstance),
                CancellationToken.None),
            await contained.CopyFileAsync(
                new PathTransferRequest(InsideInstance, InstanceRoot),
                CancellationToken.None),
            await contained.CopyDirectoryAsync(
                new PathTransferRequest(InsideInstance, InstanceRoot),
                CancellationToken.None)
        };

        var upload = await contained.OpenUploadAsync(
            new UploadOpenRequest(InstanceRoot, 1, new string('a', 64)),
            CancellationToken.None);

        foreach (var result in results)
        {
            Assert.True(result.IsErr(out var error));
            Assert.Equal("plugin.file.out_of_containment", error!.Code);
        }

        Assert.True(upload.IsErr(out var uploadError));
        Assert.Equal("plugin.file.out_of_containment", uploadError!.Code);
        Assert.Equal(0, inner.CallCount);
    }

    /// <summary>
    /// The persisted instance config and its install metadata are what the daemon reloads from and
    /// what <c>instance.query</c> answers from, so they are refused for reads as well as writes -
    /// otherwise the Low-risk <c>file.read</c> feature reads daemon state around its own gate.
    /// </summary>
    [Theory]
    [InlineData("daemon_instance.json")]
    [InlineData("daemon_instance.json.bak")]
    [InlineData("daemon_instance.install.json")]
    [InlineData("daemon_instance.install.json.bak")]
    // A deny-list is only as strong as the assumption that one file has one name. Windows breaks
    // that three ways, and each of these reached the real file before the names below were refused.
    [InlineData("DAEMON_INSTANCE.JSON")]
    [InlineData("Daemon_Instance.Json")]
    [InlineData("daemon_instance.JSON")]
    [InlineData("daemon_instance.install.JSON")]
    [InlineData("daemon_instance.json.BAK")]
    [InlineData("daemon_instance.json::$DATA")]
    [InlineData("DAEMO~1.JSO")]
    [InlineData("daemon_instance.json.")]
    [InlineData("daemon_instance.json ")]
    public async Task DaemonOwnedInstanceFilesAreRefusedForReadAndWrite(string fileName)
    {
        var inner = new RecordingFileApplication();
        var contained = Create(inner);
        var path = $"{InstanceRoot}/{fileName}";

        var read = await contained.GetFileInfoAsync(new PathRequest(path), CancellationToken.None);
        var download = await contained.OpenDownloadAsync(new DownloadOpenRequest(path), CancellationToken.None);
        var delete = await contained.DeleteFileAsync(new PathRequest(path), CancellationToken.None);
        var rename = await contained.RenameFileAsync(new PathRenameRequest(path, "stolen.json"), CancellationToken.None);
        var overwrite = await contained.OpenUploadAsync(
            new UploadOpenRequest(path, 1, new string('a', 64)),
            CancellationToken.None);
        var copyOut = await contained.CopyFileAsync(
            new PathTransferRequest(path, InsideInstance),
            CancellationToken.None);
        var forge = await contained.RenameFileAsync(
            new PathRenameRequest(InsideInstance, fileName),
            CancellationToken.None);

        Assert.True(read.IsErr(out var readError));
        Assert.Equal("plugin.file.out_of_containment", readError!.Code);
        Assert.True(download.IsErr(out var downloadError));
        Assert.Equal("plugin.file.out_of_containment", downloadError!.Code);
        Assert.True(delete.IsErr(out var deleteError));
        Assert.Equal("plugin.file.out_of_containment", deleteError!.Code);
        Assert.True(rename.IsErr(out var renameError));
        Assert.Equal("plugin.file.out_of_containment", renameError!.Code);
        Assert.True(overwrite.IsErr(out var overwriteError));
        Assert.Equal("plugin.file.out_of_containment", overwriteError!.Code);
        Assert.True(copyOut.IsErr(out var copyError));
        Assert.Equal("plugin.file.out_of_containment", copyError!.Code);
        Assert.True(forge.IsErr(out var forgeError));
        Assert.Equal("plugin.file.out_of_containment", forgeError!.Code);
        Assert.Equal(0, inner.CallCount);
    }

    [Fact]
    public void TheRefusedFileNamesAreTheOnesTheDaemonActuallyWrites()
    {
        Assert.Equal("daemon_instance.json", InstanceConfig.FileName);
        Assert.Equal("daemon_instance.install.json", InstanceInstallMetadataStore.FileName);
    }

    /// <summary>
    /// A directory under the instances root that no catalog entry describes is not instance data:
    /// creating one mints an instance the daemon never registered, and touching one reaches whatever
    /// a previous removal left behind.
    /// </summary>
    [Theory]
    // Not an identifier at all.
    [InlineData("instances/shared")]
    [InlineData("instances/shared/mods")]
    // Well-formed, absent from the catalog.
    [InlineData("instances/00000000-0000-0000-0000-0000000000ff")]
    [InlineData("instances/00000000-0000-0000-0000-0000000000ff/mods/x.jar")]
    // A catalogued identifier spelled in a form the daemon never writes is a different directory.
    [InlineData("instances/{00000000-0000-0000-0000-000000000001}/mods")]
    [InlineData("instances/00000000000000000000000000000001/mods")]
    public async Task DirectoriesTheCatalogDoesNotDescribeAreOutOfReach(string path)
    {
        var inner = new RecordingFileApplication();
        var contained = Create(inner);

        var read = await contained.GetDirectoryInfoAsync(new PathRequest(path), CancellationToken.None);
        var create = await contained.CreateDirectoryAsync(new PathRequest(path), CancellationToken.None);

        Assert.True(read.IsErr(out var readError));
        Assert.Equal("plugin.file.out_of_containment", readError!.Code);
        Assert.True(create.IsErr(out var createError));
        Assert.Equal("plugin.file.out_of_containment", createError!.Code);
        Assert.Equal(0, inner.CallCount);
        Assert.False(new CatalogSnapshotSource(CatalogedInstance).TryGet(AbsentInstance, out _));
    }

    /// <summary>
    /// The approved root is a fixed lowercase name the daemon writes itself, so it is compared with
    /// <see cref="StringComparison.Ordinal"/> on every platform. Windows supports per-directory case
    /// sensitivity, where a case-insensitive compare would admit an "Instances" sibling as if it
    /// were the approved root.
    /// </summary>
    [Theory]
    [InlineData("Instances/00000000-0000-0000-0000-000000000001/mods")]
    [InlineData("INSTANCES/00000000-0000-0000-0000-000000000001/mods")]
    public async Task ACaseVariantOfTheApprovedRootIsASibling(string path)
    {
        var inner = new RecordingFileApplication();
        var contained = Create(inner);

        var read = await contained.GetDirectoryInfoAsync(new PathRequest(path), CancellationToken.None);
        var create = await contained.CreateDirectoryAsync(new PathRequest(path), CancellationToken.None);

        Assert.True(read.IsErr(out var readError));
        Assert.Equal("plugin.file.out_of_containment", readError!.Code);
        Assert.True(create.IsErr(out var createError));
        Assert.Equal("plugin.file.out_of_containment", createError!.Code);
        Assert.Equal(0, inner.CallCount);
    }

    [Fact]
    public async Task ACatalogedInstanceDirectoryIsReadable()
    {
        var inner = new RecordingFileApplication();
        var contained = Create(inner);

        var listing = await contained.GetDirectoryInfoAsync(new PathRequest(InstanceRoot), CancellationToken.None);

        Assert.True(listing.IsErr(out var error));
        Assert.Equal("test.inner", error!.Code);
        Assert.Equal(1, inner.CallCount);
    }

    [Fact]
    public async Task InstanceScopedPathsPassThrough()
    {
        var inner = new RecordingFileApplication();
        var contained = Create(inner);

        var read = await contained.GetFileInfoAsync(new PathRequest(InsideInstance), CancellationToken.None);
        var write = await contained.DeleteFileAsync(new PathRequest(InsideInstance), CancellationToken.None);
        var rename = await contained.RenameFileAsync(
            new PathRenameRequest(InsideInstance, "y.jar"),
            CancellationToken.None);

        Assert.True(read.IsErr(out var readError));
        Assert.Equal("test.inner", readError!.Code);
        Assert.True(write.IsErr(out var writeError));
        Assert.Equal("test.inner", writeError!.Code);
        Assert.True(rename.IsErr(out var renameError));
        Assert.Equal("test.inner", renameError!.Code);
        Assert.Equal(3, inner.CallCount);
    }

    [Fact]
    public async Task TransferRejectsWhenEitherEndEscapes()
    {
        var inner = new RecordingFileApplication();
        var contained = Create(inner);

        var exfiltrate = await contained.CopyFileAsync(
            new PathTransferRequest("audit/audit.jsonl", InsideInstance),
            CancellationToken.None);
        var overwrite = await contained.MoveFileAsync(
            new PathTransferRequest(InsideInstance, "automation/policies.json"),
            CancellationToken.None);

        Assert.True(exfiltrate.IsErr(out var exfiltrateError));
        Assert.Equal("plugin.file.out_of_containment", exfiltrateError!.Code);
        Assert.True(overwrite.IsErr(out var overwriteError));
        Assert.Equal("plugin.file.out_of_containment", overwriteError!.Code);
        Assert.Equal(0, inner.CallCount);
    }

    /// <summary>
    /// Every open is released from one barrier so they all pass admission before any of them
    /// registers. Reading the count, awaiting the inner open and registering afterwards lets every
    /// caller observe the same headroom, which is exactly what a sequential loop never reproduces.
    /// </summary>
    [Fact]
    public async Task ConcurrentOpensCannotExceedTheSessionCap()
    {
        const int attempts = 4 * ContainedPluginFileApplication.MaximumConcurrentSessions;
        var inner = new RecordingFileApplication();
        var contained = Create(inner);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = attempts;

        void Settle()
        {
            if (Interlocked.Decrement(ref pending) == 0)
                settled.TrySetResult();
        }

        inner.OpenGate = gate;
        inner.OnOpenEntered = Settle;

        var opens = Enumerable.Range(0, attempts)
            .Select(index => Task.Run(async () =>
            {
                var result = await contained.OpenDownloadAsync(
                    new DownloadOpenRequest($"{InstanceRoot}/world{index}.zip"),
                    CancellationToken.None);
                // An open parked inside the fake cannot have completed while the barrier is closed,
                // so reaching here before the release means this attempt was refused up front.
                Settle();
                return result;
            }))
            .ToArray();

        // Every attempt has now either been refused or is parked past admission inside the fake.
        await settled.Task;
        gate.TrySetResult();
        var results = await Task.WhenAll(opens);

        var admitted = results.Count(result => result.IsOk(out _));
        Assert.Equal(ContainedPluginFileApplication.MaximumConcurrentSessions, admitted);
        // Slots are reserved before the inner open, so the refused attempts never start the file
        // handle and hashing work either.
        Assert.Equal(ContainedPluginFileApplication.MaximumConcurrentSessions, inner.OpenCount);
        foreach (var result in results.Where(result => result.IsErr(out _)))
        {
            Assert.True(result.IsErr(out var error));
            Assert.Equal("plugin.file.session_limit", error!.Code);
        }
    }

    /// <summary>
    /// A cancelled close throws before the coordinator removes its own session, so the inner handle
    /// and any staging bytes live on. Freeing the outer slot there would let a plugin repeat the
    /// call and walk straight past its quota.
    /// </summary>
    [Fact]
    public async Task ACancelledCloseDoesNotFreeTheSlot()
    {
        var inner = new RecordingFileApplication();
        var contained = Create(inner);
        var opened = await contained.OpenDownloadAsync(
            new DownloadOpenRequest($"{InstanceRoot}/world.zip"),
            CancellationToken.None);
        Assert.True(opened.IsOk(out var session));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => contained.CloseDownloadAsync(session!.SessionId, cancellation.Token));

        // Still owned: the plugin can retry, and the slot is still charged against its budget.
        var retried = await contained.CloseDownloadAsync(session!.SessionId, CancellationToken.None);
        Assert.True(retried.IsOk(out _));
        Assert.Contains(session.SessionId, inner.ClosedDownloads);
    }

    [Fact]
    public async Task ACancelledCancelUploadKeepsTheSlotCharged()
    {
        var inner = new RecordingFileApplication();
        var contained = Create(inner);
        var path = $"{InstanceRoot}/world.zip";
        var first = await contained.OpenUploadAsync(
            new UploadOpenRequest(path, 1, new string('a', 64)),
            CancellationToken.None);
        Assert.True(first.IsOk(out var session));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => contained.CancelUploadAsync(session!.SessionId, cancellation.Token));

        // Fill the remaining budget; the failed cancel must still be holding its own slot.
        for (var opened = 1; opened < ContainedPluginFileApplication.MaximumConcurrentSessions; opened++)
        {
            var admitted = await contained.OpenUploadAsync(
                new UploadOpenRequest(path, 1, new string('a', 64)),
                CancellationToken.None);
            Assert.True(admitted.IsOk(out _));
        }

        var refused = await contained.OpenUploadAsync(
            new UploadOpenRequest(path, 1, new string('a', 64)),
            CancellationToken.None);

        Assert.True(refused.IsErr(out var error));
        Assert.Equal("plugin.file.session_limit", error!.Code);
    }

    /// <summary>
    /// Revocation must not race an open that is already past admission: it closes admission, waits
    /// for the in-flight open to settle, and the open that settles into a revoking facade closes its
    /// own inner session instead of registering it.
    /// </summary>
    [Fact]
    public async Task ReleaseWaitsForAnInFlightOpenAndClaimsIt()
    {
        var inner = new RecordingFileApplication();
        var contained = Create(inner);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        inner.OpenGate = gate;
        inner.OnOpenEntered = () => entered.TrySetResult();

        var open = contained.OpenDownloadAsync(
            new DownloadOpenRequest($"{InstanceRoot}/world.zip"),
            CancellationToken.None);
        await entered.Task;

        var release = contained.ReleaseSessionsAsync();
        // The reservation this open holds is what the sweep waits on, so the release cannot have
        // completed while the open is still parked.
        Assert.False(release.IsCompleted);

        gate.TrySetResult();
        var result = await open;
        await release;

        Assert.True(result.IsErr(out var error));
        Assert.Equal("plugin.file.sessions_revoked", error!.Code);
        Assert.Single(inner.ClosedDownloads);
        var orphan = inner.ClosedDownloads.Single();
        var afterRelease = await contained.ReadDownloadChunkAsync(
            new DownloadChunkRequest(orphan, 0, 1),
            CancellationToken.None);
        Assert.True(afterRelease.IsErr(out var afterError));
        Assert.Equal("plugin.file.session_not_owned", afterError!.Code);
    }

    [Fact]
    public async Task RevokedFacadesRefuseFurtherOpens()
    {
        var inner = new RecordingFileApplication();
        var contained = Create(inner);

        await contained.ReleaseSessionsAsync();
        var result = await contained.OpenDownloadAsync(
            new DownloadOpenRequest($"{InstanceRoot}/world.zip"),
            CancellationToken.None);

        Assert.True(result.IsErr(out var error));
        Assert.Equal("plugin.file.sessions_revoked", error!.Code);
        Assert.Equal(0, inner.OpenCount);
    }

    /// <summary>
    /// A registered session whose inner release never returns must not hold the revocation open. The
    /// deadline has to span the drain and not only the wait for in-flight opens: a download close
    /// hashes the file and an upload cancel disposes a staging stream, so either can park on a
    /// stalled volume — which is the failure this revocation exists to prevent, and every caller is
    /// on a stop or shutdown path that is itself deadline-bounded.
    /// </summary>
    [Fact]
    public async Task AWedgedInnerReleaseDoesNotHoldTheRevocationOpen()
    {
        var inner = new RecordingFileApplication();
        var contained = Create(inner, TimeSpan.FromMilliseconds(200));
        var path = $"{InstanceRoot}/world.zip";
        Assert.True(
            (await contained.OpenDownloadAsync(new DownloadOpenRequest(path), CancellationToken.None)).IsOk(out _));

        inner.ReleaseGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var release = contained.ReleaseSessionsAsync();
        // Generous, so a slow agent cannot pass this by accident: the point is that the release
        // returns without the gate ever being opened, not that it returns quickly.
        var settled = await Task.WhenAny(release, Task.Delay(TimeSpan.FromSeconds(30)));
        Assert.Same(release, settled);
        await release;

        // Admission is shut even though the inner handle is still pinned.
        var refused = await contained.OpenDownloadAsync(new DownloadOpenRequest(path), CancellationToken.None);
        Assert.True(refused.IsErr(out var error));
        Assert.Equal("plugin.file.sessions_revoked", error!.Code);

        // The sweep is held rather than dropped, so the handle still comes back when the stalled
        // call finally returns.
        Assert.False(contained.PendingRelease.IsCompleted);
        inner.ReleaseGate.SetResult();
        await contained.PendingRelease;
        Assert.Single(inner.ClosedDownloads);
    }

    /// <summary>
    /// A second revocation must join the first sweep. The first clears the tracked sets before its
    /// first inner call, so a second drain would either find nothing to release or release a
    /// straggler twice.
    /// </summary>
    [Fact]
    public async Task ASecondRevocationJoinsTheFirstSweep()
    {
        var inner = new RecordingFileApplication();
        var contained = Create(inner, TimeSpan.FromMilliseconds(200));
        var path = $"{InstanceRoot}/world.zip";
        Assert.True(
            (await contained.OpenDownloadAsync(new DownloadOpenRequest(path), CancellationToken.None)).IsOk(out _));

        inner.ReleaseGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await contained.ReleaseSessionsAsync();
        var firstSweep = contained.PendingRelease;
        await contained.ReleaseSessionsAsync();

        Assert.Same(firstSweep, contained.PendingRelease);
        inner.ReleaseGate.SetResult();
        await contained.PendingRelease;
        Assert.Single(inner.ClosedDownloads);
        // The fake counts the open too, so two calls is one open plus exactly one close: the second
        // revocation added no inner traffic of its own.
        Assert.Equal(2, inner.CallCount);
    }

    /// <summary>
    /// A Windows 8.3 short name is a second name for the same file, and it need not contain a tilde,
    /// so no name-shape rule can recognise one. Planting <c>CFGXJSON.JSN</c> on the instance config
    /// needs no elevation and works even on a volume with 8.3 auto-generation switched off, so the
    /// deny-list has to be applied to the canonicalised path rather than the spelling the caller sent.
    /// </summary>
    [Fact]
    public async Task ATildeFreeShortNameCannotReachADaemonOwnedFile()
    {
        // 8.3 short names are an NTFS feature; there is nothing to alias elsewhere.
        if (!OperatingSystem.IsWindows())
            return;

        var instanceDirectory = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "daemon",
            "instances",
            CatalogedInstance.ToString());
        Directory.CreateDirectory(instanceDirectory);
        var config = Path.Combine(instanceDirectory, "daemon_instance.json");
        File.WriteAllText(config, "{}");

        // Asserted rather than skipped: if the alias cannot be planted the test is vacuous, and a
        // vacuous pass would hide the regression this exists to catch. Measured to succeed unelevated
        // on NTFS whether or not the volume has 8.3 auto-generation enabled.
        Assert.True(
            TryPlantShortName(config, "CFGXJSON.JSN"),
            "could not plant a short name, so this test proves nothing on this volume");

        try
        {
            var inner = new RecordingFileApplication();
            var contained = Create(inner);

            var read = await contained.GetFileInfoAsync(
                new PathRequest($"{InstanceRoot}/CFGXJSON.JSN"),
                CancellationToken.None);

            Assert.True(read.IsErr(out var error));
            Assert.Equal("plugin.file.out_of_containment", error!.Code);
            Assert.Equal(0, inner.CallCount);
        }
        finally
        {
            // This is the only test here that writes a real instance directory under the daemon root.
            // Leaving a daemon_instance.json behind would be state another test could scan.
            Directory.Delete(instanceDirectory, true);
        }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFileW(
        string path,
        uint access,
        uint share,
        IntPtr security,
        uint disposition,
        uint flags,
        IntPtr template);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern bool SetFileShortName(IntPtr file, string shortName);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    private static bool TryPlantShortName(string path, string shortName)
    {
        const uint delete = 0x00010000;
        const uint shareAll = 7;
        const uint openExisting = 3;
        const uint backupSemantics = 0x02000000;

        var handle = CreateFileW(path, delete, shareAll, IntPtr.Zero, openExisting, backupSemantics, IntPtr.Zero);
        if (handle == new IntPtr(-1))
            return false;

        try
        {
            return SetFileShortName(handle, shortName);
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    /// <summary>
    /// The coordinator expires sessions on its own schedule and tells nobody, so the facade cannot
    /// learn a lease lapsed by being told. A plugin that lets its whole budget expire instead of
    /// closing it must not be capped for the rest of its life: reaching the cap has to make the
    /// facade ask the inner application about the lapsed leases before it refuses.
    /// </summary>
    /// <remarks>
    /// Deliberately never closes or cancels the old sessions — that is the path that already worked.
    /// The only thing that happens here is the clock moving.
    /// </remarks>
    [Fact]
    public async Task LapsedLeasesAreReconciledWhenTheCapIsReached()
    {
        var time = new ManualTimeProvider(DateTimeOffset.Parse("2026-07-30T00:00:00+00:00"));
        var inner = new RecordingFileApplication { Clock = time, LeaseDuration = TimeSpan.FromMinutes(5) };
        var contained = Create(inner, time);
        var path = $"{InstanceRoot}/world.zip";

        for (var opened = 0; opened < ContainedPluginFileApplication.MaximumConcurrentSessions; opened++)
        {
            var admitted = await contained.OpenDownloadAsync(new DownloadOpenRequest(path), CancellationToken.None);
            Assert.True(admitted.IsOk(out _));
        }

        var atCap = await contained.OpenDownloadAsync(new DownloadOpenRequest(path), CancellationToken.None);
        Assert.True(atCap.IsErr(out var capError));
        Assert.Equal("plugin.file.session_limit", capError!.Code);

        time.Advance(TimeSpan.FromMinutes(6));

        var reopened = await contained.OpenDownloadAsync(new DownloadOpenRequest(path), CancellationToken.None);
        Assert.True(reopened.IsOk(out _));
        Assert.Equal(
            ContainedPluginFileApplication.MaximumConcurrentSessions,
            inner.ClosedDownloads.Count);
    }

    /// <summary>
    /// A clock past <c>ExpiresAt</c> is a reason to ask the inner application, never the answer. If it
    /// reports anything other than success or already-gone the handle is still live, so the slot must
    /// stay held — otherwise a plugin holds more live handles than the cap by simply waiting.
    /// </summary>
    [Fact]
    public async Task ALapsedLeaseTheInnerStillHoldsKeepsItsSlot()
    {
        var time = new ManualTimeProvider(DateTimeOffset.Parse("2026-07-30T00:00:00+00:00"));
        var inner = new RecordingFileApplication { Clock = time, LeaseDuration = TimeSpan.FromMinutes(5) };
        var contained = Create(inner, time);
        var path = $"{InstanceRoot}/world.zip";

        for (var opened = 0; opened < ContainedPluginFileApplication.MaximumConcurrentSessions; opened++)
            Assert.True(
                (await contained.OpenDownloadAsync(new DownloadOpenRequest(path), CancellationToken.None))
                .IsOk(out _));

        time.Advance(TimeSpan.FromMinutes(6));
        inner.TerminateFailure = new ConflictDaemonError("test.busy", "the handle is still in use");

        var refused = await contained.OpenDownloadAsync(new DownloadOpenRequest(path), CancellationToken.None);

        Assert.True(refused.IsErr(out var error));
        Assert.Equal("plugin.file.session_limit", error!.Code);
        Assert.Empty(inner.ClosedDownloads);
    }

    /// <summary>
    /// The coordinator expires sessions on its own schedule. A close that reports the session
    /// already gone is terminal, so the slot must be returned rather than pinned until the plugin
    /// stops.
    /// </summary>
    [Fact]
    public async Task ASessionExpiredUnderneathTheFacadeDoesNotWedgeItsSlot()
    {
        var inner = new RecordingFileApplication();
        var contained = Create(inner);
        var path = $"{InstanceRoot}/world.zip";
        var sessions = new List<Guid>();
        for (var opened = 0; opened < ContainedPluginFileApplication.MaximumConcurrentSessions; opened++)
        {
            var admitted = await contained.OpenDownloadAsync(new DownloadOpenRequest(path), CancellationToken.None);
            Assert.True(admitted.IsOk(out var session));
            sessions.Add(session!.SessionId);
        }

        inner.Expire(sessions[0]);
        var closed = await contained.CloseDownloadAsync(sessions[0], CancellationToken.None);

        Assert.True(closed.IsErr(out var closeError));
        Assert.Equal(DaemonErrorKind.NotFound, closeError!.Kind);
        var reopened = await contained.OpenDownloadAsync(new DownloadOpenRequest(path), CancellationToken.None);
        Assert.True(reopened.IsOk(out _));
    }

    [Fact]
    public async Task ForeignSessionsCannotBeReadOrClosed()
    {
        var inner = new RecordingFileApplication();
        var contained = Create(inner);
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
        var contained = Create(inner);
        var path = $"{InstanceRoot}/world.zip";
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
            new CatalogSnapshotSource(CatalogedInstance),
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
        var seizeInstance = await authorizer.Host.FileWrites.DeleteDirectoryAsync(
            new DeleteDirectoryRequest(InstanceRoot, Recursive: true),
            CancellationToken.None);
        var readConfig = await authorizer.Host.FileReads.GetFileInfoAsync(
            new PathRequest($"{InstanceRoot}/{InstanceConfig.FileName}"),
            CancellationToken.None);

        Assert.True(escape.IsErr(out var error));
        Assert.Equal("plugin.file.out_of_containment", error!.Code);
        Assert.True(seizeInstance.IsErr(out var seizeError));
        Assert.Equal("plugin.file.out_of_containment", seizeError!.Code);
        Assert.True(readConfig.IsErr(out var readError));
        Assert.Equal("plugin.file.out_of_containment", readError!.Code);
        Assert.Equal(0, inner.CallCount);
    }

    /// <summary>
    /// Containment consults the catalog whatever features the plugin holds, so a plugin granted the
    /// file features without <c>instance.query</c> is confined the same way.
    /// </summary>
    [Fact]
    public async Task ContainmentDoesNotDependOnTheInstanceQueryFeature()
    {
        var inner = new RecordingFileApplication();
        var identity = new PluginIdentity("community.file-only", "1.0.0");
        var authorizer = new PluginApplicationAuthorizer(
            identity,
            ["file.read"],
            new CallerContextFactory(new VerifiedPrincipalAuthority()),
            new CatalogSnapshotSource(CatalogedInstance),
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

        var allowed = await authorizer.Host.FileReads.GetDirectoryInfoAsync(
            new PathRequest($"{InstanceRoot}/mods"),
            CancellationToken.None);
        var refused = await authorizer.Host.FileReads.GetDirectoryInfoAsync(
            new PathRequest($"instances/{AbsentInstance}/mods"),
            CancellationToken.None);

        Assert.True(allowed.IsErr(out var allowedError));
        Assert.Equal("test.inner", allowedError!.Code);
        Assert.True(refused.IsErr(out var refusedError));
        Assert.Equal("plugin.file.out_of_containment", refusedError!.Code);
    }

    private static ContainedPluginFileApplication Create(IFileApplication inner) =>
        new(inner, new CatalogSnapshotSource(CatalogedInstance));

    private static ContainedPluginFileApplication Create(IFileApplication inner, TimeSpan releaseDeadline) =>
        new(inner, new CatalogSnapshotSource(CatalogedInstance), releaseDeadline: releaseDeadline);

    private static ContainedPluginFileApplication Create(IFileApplication inner, TimeProvider timeProvider) =>
        new(inner, new CatalogSnapshotSource(CatalogedInstance), timeProvider: timeProvider);

    private sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        internal void Advance(TimeSpan delta) => _now += delta;
    }

    private sealed class CatalogSnapshotSource : IInstanceSnapshotSource
    {
        private readonly StatePublisher<InstanceCatalogSnapshot> _publisher;

        internal CatalogSnapshotSource(params Guid[] instanceIds) =>
            _publisher = new StatePublisher<InstanceCatalogSnapshot>(new InstanceCatalogSnapshot(
                instanceIds.Select(id => new KeyValuePair<Guid, InstanceSnapshot>(
                    id,
                    new InstanceSnapshot(id, "fixture", InstanceType.Universal, "1", InstanceStatus.Stopped, false)))));

        public PublishedState<InstanceCatalogSnapshot> Current => _publisher.Current;

        public bool TryGet(Guid instanceId, [NotNullWhen(true)] out InstanceSnapshot? snapshot) =>
            Current.Value.TryGet(instanceId, out snapshot);
    }

    private sealed class RecordingFileApplication : IFileApplication
    {
        private static readonly DaemonError Inner = new ValidationDaemonError("test.inner", "inner reached");

        private readonly ConcurrentDictionary<Guid, byte> _liveDownloads = new();
        private readonly ConcurrentDictionary<Guid, byte> _liveUploads = new();
        private int _callCount;
        private int _openCount;

        /// <summary>
        /// When set, every open parks on this barrier after announcing its arrival, so a test can
        /// hold several opens past admission at once.
        /// </summary>
        internal TaskCompletionSource? OpenGate { get; set; }

        /// <summary>
        /// When set, every terminal release parks on this barrier after being counted, so a test can
        /// hold a drain the way a stalled volume would. The session stays live until it is released.
        /// </summary>
        internal TaskCompletionSource? ReleaseGate { get; set; }

        /// <summary>
        /// When set, every terminal release fails with this error and keeps the session live, the way
        /// a coordinator that has not actually let go of the handle would answer.
        /// </summary>
        internal DaemonError? TerminateFailure { get; set; }

        /// <summary>The clock the minted leases are dated against.</summary>
        internal TimeProvider Clock { get; set; } = TimeProvider.System;

        internal TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(5);

        internal Action? OnOpenEntered { get; set; }

        internal int CallCount => Volatile.Read(ref _callCount);

        internal int OpenCount => Volatile.Read(ref _openCount);

        internal ConcurrentQueue<Guid> ClosedDownloads { get; } = new();

        internal ConcurrentQueue<Guid> CanceledUploads { get; } = new();

        /// <summary>Drops a session the way the coordinator's expiry sweep would.</summary>
        internal void Expire(Guid sessionId)
        {
            _liveDownloads.TryRemove(sessionId, out _);
            _liveUploads.TryRemove(sessionId, out _);
        }

        public Task<Result<DirectoryDetails, DaemonError>> GetDirectoryInfoAsync(
            PathRequest request,
            CancellationToken cancellationToken) => Fail<DirectoryDetails>();

        public Task<Result<FileDetails, DaemonError>> GetFileInfoAsync(
            PathRequest request,
            CancellationToken cancellationToken) => Fail<FileDetails>();

        public async Task<Result<DownloadSession, DaemonError>> OpenDownloadAsync(
            DownloadOpenRequest request,
            CancellationToken cancellationToken)
        {
            await EnterOpenAsync();
            var sessionId = Guid.NewGuid();
            _liveDownloads[sessionId] = 0;
            return Result.Ok<DownloadSession, DaemonError>(
                new DownloadSession(sessionId, 1, new string('a', 64), 1, Clock.GetUtcNow() + LeaseDuration));
        }

        public Task<Result<DownloadChunk, DaemonError>> ReadDownloadChunkAsync(
            DownloadChunkRequest request,
            CancellationToken cancellationToken) => Fail<DownloadChunk>();

        public async Task<Result<Unit, DaemonError>> CloseDownloadAsync(
            Guid sessionId,
            CancellationToken cancellationToken)
        {
            // Mirrors FileSessionCoordinator: a cancelled token throws before the session is removed.
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _callCount);
            if (ReleaseGate is { } gate)
                await gate.Task.ConfigureAwait(false);
            if (TerminateFailure is { } failure)
                return Result.Err<Unit, DaemonError>(failure);
            if (!_liveDownloads.TryRemove(sessionId, out _))
                return Result.Err<Unit, DaemonError>(SessionNotFound(sessionId));

            ClosedDownloads.Enqueue(sessionId);
            return Result.Ok<Unit, DaemonError>(default);
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

        public async Task<Result<UploadSession, DaemonError>> OpenUploadAsync(
            UploadOpenRequest request,
            CancellationToken cancellationToken)
        {
            await EnterOpenAsync();
            var sessionId = Guid.NewGuid();
            _liveUploads[sessionId] = 0;
            return Result.Ok<UploadSession, DaemonError>(
                new UploadSession(sessionId, 1, Clock.GetUtcNow() + LeaseDuration));
        }

        public Task<Result<Unit, DaemonError>> WriteUploadChunkAsync(
            UploadChunkRequest request,
            CancellationToken cancellationToken) => Fail<Unit>();

        public Task<Result<Unit, DaemonError>> CloseUploadAsync(
            Guid sessionId,
            CancellationToken cancellationToken) => EndUpload(sessionId, cancellationToken);

        public Task<Result<Unit, DaemonError>> CancelUploadAsync(
            Guid sessionId,
            CancellationToken cancellationToken) => EndUpload(sessionId, cancellationToken);

        private async Task<Result<Unit, DaemonError>> EndUpload(Guid sessionId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _callCount);
            if (ReleaseGate is { } gate)
                await gate.Task.ConfigureAwait(false);
            if (TerminateFailure is { } failure)
                return Result.Err<Unit, DaemonError>(failure);
            if (!_liveUploads.TryRemove(sessionId, out _))
                return Result.Err<Unit, DaemonError>(SessionNotFound(sessionId));

            CanceledUploads.Enqueue(sessionId);
            return Result.Ok<Unit, DaemonError>(default);
        }

        private async Task EnterOpenAsync()
        {
            Interlocked.Increment(ref _callCount);
            Interlocked.Increment(ref _openCount);
            var gate = OpenGate;
            if (gate is null)
                return;

            OnOpenEntered?.Invoke();
            await gate.Task;
        }

        private static NotFoundDaemonError SessionNotFound(Guid sessionId) =>
            new("file.session.not_found", $"The file session '{sessionId}' was not found.");

        private Task<Result<T, DaemonError>> Fail<T>() where T : notnull
        {
            Interlocked.Increment(ref _callCount);
            return Task.FromResult(Result.Err<T, DaemonError>(Inner));
        }
    }
}
