using System.IO.Compression;
using MCServerLauncher.Common.Contracts.Backup;
using MCServerLauncher.Daemon.ApplicationCore.Backups;

namespace MCServerLauncher.ProtocolTests;

public sealed class BackupArchiveStoreTests
{
    [Fact]
    public async Task CreateArchive_RoundTripsManifestExcludesStagingAndSurvivesReload()
    {
        var root = Directory.CreateTempSubdirectory("mcsl-backup-store-").FullName;
        var workingDirectory = Directory.CreateTempSubdirectory("mcsl-backup-instance-").FullName;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(workingDirectory, "server.jar"), "core-bytes");
            await File.WriteAllTextAsync(Path.Combine(workingDirectory, "daemon_instance.json"), "{\"name\":\"demo\"}");
            Directory.CreateDirectory(Path.Combine(workingDirectory, "world"));
            await File.WriteAllTextAsync(Path.Combine(workingDirectory, "world", "level.dat"), "level-bytes");
            Directory.CreateDirectory(Path.Combine(workingDirectory, ".instance-update-stale"));
            await File.WriteAllTextAsync(
                Path.Combine(workingDirectory, ".instance-update-stale", "staged.jar"),
                "must-not-archive");
            Directory.CreateDirectory(Path.Combine(workingDirectory, ".restore-stale"));
            await File.WriteAllTextAsync(Path.Combine(workingDirectory, ".removing-stale"), "must-not-archive");

            var instanceId = Guid.NewGuid();
            var store = CreateStore(root);
            var created = await store.CreateArchiveAsync(
                instanceId,
                "demo",
                "1.21",
                workingDirectory,
                "daemon_instance.json",
                CancellationToken.None);

            Assert.True(created.IsOk(out var manifest));
            Assert.Equal(instanceId, manifest!.InstanceId);
            Assert.Equal(BackupArchiveStore.CurrentManifestVersion, manifest.ManifestVersion);
            Assert.Equal("1.21", manifest.InstanceVersion);
            Assert.NotNull(manifest.ConfigSha256);
            Assert.Equal(
                new[] { "daemon_instance.json", "server.jar", "world/level.dat" },
                manifest.Files.Select(static file => file.RelativePath).OrderBy(static path => path, StringComparer.Ordinal));
            Assert.Equal("deflate", manifest.CompressionMethod);
            Assert.True(File.Exists(store.GetArchivePath(manifest.ArchiveId)));

            var verified = await store.VerifyArchiveAsync(manifest.ArchiveId, CancellationToken.None);
            Assert.True(verified.IsOk(out _));

            var reloaded = CreateStore(root);
            Assert.True(reloaded.Get(manifest.ArchiveId).IsOk(out var persisted));
            // ImmutableArray members defeat whole-record equality; compare the fields.
            Assert.Equal(manifest with { Files = default }, persisted! with { Files = default });
            Assert.True(manifest.Files.SequenceEqual(persisted.Files));
            Assert.Equal(manifest.ArchiveId, Assert.Single(reloaded.List(instanceId)).ArchiveId);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(workingDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task VerifyArchive_DetectsTamperedPayloadAndMissingPayload()
    {
        var root = Directory.CreateTempSubdirectory("mcsl-backup-tamper-").FullName;
        var workingDirectory = Directory.CreateTempSubdirectory("mcsl-backup-instance-").FullName;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(workingDirectory, "server.jar"), "core-bytes");
            var store = CreateStore(root);
            var created = await store.CreateArchiveAsync(
                Guid.NewGuid(), "demo", "1.21", workingDirectory, "daemon_instance.json", CancellationToken.None);
            Assert.True(created.IsOk(out var manifest));

            await File.AppendAllTextAsync(store.GetArchivePath(manifest!.ArchiveId), "tamper");
            var tampered = await store.VerifyArchiveAsync(manifest.ArchiveId, CancellationToken.None);
            Assert.True(tampered.IsErr(out var checksumError));
            Assert.Equal("backup.checksum_mismatch", checksumError!.Code);

            File.Delete(store.GetArchivePath(manifest.ArchiveId));
            var missing = await store.VerifyArchiveAsync(manifest.ArchiveId, CancellationToken.None);
            Assert.True(missing.IsErr(out var missingError));
            Assert.Equal("backup.archive_missing", missingError!.Code);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(workingDirectory, recursive: true);
        }
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("world/../../escape.txt")]
    [InlineData("C:/absolute/escape.txt")]
    public void ExtractToStaging_RejectsEntriesThatEscapeTheStagingDirectory(string entryName)
    {
        var root = Directory.CreateTempSubdirectory("mcsl-backup-extract-escape-").FullName;
        var staging = Directory.CreateTempSubdirectory("mcsl-backup-staging-").FullName;
        try
        {
            var store = CreateStore(root);
            var archiveId = WriteRawArchive(store, archive =>
            {
                var entry = archive.CreateEntry(entryName);
                using var writer = new StreamWriter(entry.Open());
                writer.Write("escape-bytes");
            });

            var extracted = store.ExtractToStaging(archiveId, staging, CancellationToken.None);
            Assert.True(extracted.IsErr(out var error));
            Assert.Equal("backup.archive_entry_invalid", error!.Code);
            Assert.Empty(Directory.EnumerateFileSystemEntries(staging, "*", SearchOption.AllDirectories)
                .Where(static path => !Directory.Exists(path)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(staging, recursive: true);
        }
    }

    [Fact]
    public void ExtractToStaging_RejectsSymlinkEntriesAndExtractsCleanArchives()
    {
        var root = Directory.CreateTempSubdirectory("mcsl-backup-extract-").FullName;
        var staging = Directory.CreateTempSubdirectory("mcsl-backup-staging-").FullName;
        try
        {
            var store = CreateStore(root);
            var linkArchiveId = WriteRawArchive(store, archive =>
            {
                var entry = archive.CreateEntry("evil-link");
                // Unix mode bits live in the upper 16 bits; 0xA1FF = lrwxrwxrwx.
                entry.ExternalAttributes = 0xA1FF << 16;
                using var writer = new StreamWriter(entry.Open());
                writer.Write("/etc/passwd");
            });
            var rejected = store.ExtractToStaging(linkArchiveId, staging, CancellationToken.None);
            Assert.True(rejected.IsErr(out var error));
            Assert.Equal("backup.archive_entry_invalid", error!.Code);

            var cleanArchiveId = WriteRawArchive(store, archive =>
            {
                var entry = archive.CreateEntry("world/level.dat");
                using var writer = new StreamWriter(entry.Open());
                writer.Write("level-bytes");
            });
            var extracted = store.ExtractToStaging(cleanArchiveId, staging, CancellationToken.None);
            Assert.True(extracted.IsOk(out _));
            Assert.Equal("level-bytes", File.ReadAllText(Path.Combine(staging, "world", "level.dat")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(staging, recursive: true);
        }
    }

    [Fact]
    public async Task Prune_AppliesAgeCountAndByteCapsButNeverTouchesPinnedArchives()
    {
        var root = Directory.CreateTempSubdirectory("mcsl-backup-prune-").FullName;
        var workingDirectory = Directory.CreateTempSubdirectory("mcsl-backup-instance-").FullName;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(workingDirectory, "server.jar"), new string('x', 512));
            var time = new ManualTimeProvider(DateTimeOffset.Parse("2026-07-01T00:00:00Z"));
            var config = new DaemonBackupConfig { RetentionDays = 10, MaximumCount = 16, MaximumBytes = 100_000 };
            var store = CreateStore(root, config, time);

            var instanceId = Guid.NewGuid();
            var manifests = new List<BackupArchiveManifest>();
            for (var index = 0; index < 4; index++)
            {
                var created = await store.CreateArchiveAsync(
                    instanceId, "demo", "1.21", workingDirectory, "daemon_instance.json", CancellationToken.None);
                Assert.True(created.IsOk(out var manifest));
                manifests.Add(manifest!);
                time.Advance(TimeSpan.FromDays(4));
            }

            // Ages at prune time: 16, 12, 8, and 4 days. The two oldest exceed the age cap, but the
            // oldest is pinned by an active restore plan and must survive every cap.
            var pinned = new HashSet<Guid> { manifests[0].ArchiveId };
            var pruned = store.Prune(pinned);
            Assert.True(pruned.IsOk(out var removed));
            Assert.Equal(new[] { manifests[1].ArchiveId }, removed);
            Assert.True(store.Get(manifests[0].ArchiveId).IsOk(out _));
            Assert.False(File.Exists(store.GetArchivePath(manifests[1].ArchiveId)));

            // Count cap: pinned + two recent = 3 retained entries against MaximumCount 2; the oldest
            // unpinned survivor is removed, and the pinned archive never counts as removable.
            var countConfig = new DaemonBackupConfig { RetentionDays = 365, MaximumCount = 2, MaximumBytes = 100_000 };
            var countStore = CreateStore(root, countConfig, time);
            var countPruned = countStore.Prune(pinned);
            Assert.True(countPruned.IsOk(out var countRemoved));
            Assert.Equal(new[] { manifests[2].ArchiveId }, countRemoved);

            var byteConfig = new DaemonBackupConfig { RetentionDays = 365, MaximumCount = 16, MaximumBytes = 1 };
            var byteStore = CreateStore(root, byteConfig, time);
            var bytePruned = byteStore.Prune(pinned);
            Assert.True(bytePruned.IsOk(out var byteRemoved));
            Assert.Equal(new[] { manifests[3].ArchiveId }, byteRemoved);
            Assert.True(byteStore.Get(manifests[0].ArchiveId).IsOk(out _));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(workingDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Load_SweepsOrphanedPayloadsAndDropsManifestsWithoutPayloads()
    {
        var root = Directory.CreateTempSubdirectory("mcsl-backup-sweep-").FullName;
        var workingDirectory = Directory.CreateTempSubdirectory("mcsl-backup-instance-").FullName;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(workingDirectory, "server.jar"), "core-bytes");
            var store = CreateStore(root);
            var kept = await store.CreateArchiveAsync(
                Guid.NewGuid(), "demo", "1.21", workingDirectory, "daemon_instance.json", CancellationToken.None);
            Assert.True(kept.IsOk(out var keptManifest));
            var lost = await store.CreateArchiveAsync(
                Guid.NewGuid(), "demo", "1.21", workingDirectory, "daemon_instance.json", CancellationToken.None);
            Assert.True(lost.IsOk(out var lostManifest));

            File.Delete(store.GetArchivePath(lostManifest!.ArchiveId));
            var orphanZip = Path.Combine(root, "archives", $"{Guid.NewGuid():D}.zip");
            await File.WriteAllTextAsync(orphanZip, "orphan");
            var strayTemp = Path.Combine(root, "archives", "stray.tmp");
            await File.WriteAllTextAsync(strayTemp, "stray");

            var reloaded = CreateStore(root);
            Assert.True(reloaded.Get(keptManifest!.ArchiveId).IsOk(out _));
            Assert.True(reloaded.Get(lostManifest.ArchiveId).IsErr(out var lostError));
            Assert.Equal("backup.archive_not_found", lostError!.Code);
            Assert.False(File.Exists(orphanZip));
            Assert.False(File.Exists(strayTemp));

            var thirdLoad = CreateStore(root);
            Assert.Single(thirdLoad.List());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(workingDirectory, recursive: true);
        }
    }

    private static BackupArchiveStore CreateStore(
        string root,
        DaemonBackupConfig? config = null,
        TimeProvider? timeProvider = null) =>
        new(config ?? new DaemonBackupConfig(), timeProvider, root);

    private static Guid WriteRawArchive(BackupArchiveStore store, Action<ZipArchive> populate)
    {
        var archiveId = Guid.NewGuid();
        var path = store.GetArchivePath(archiveId);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        populate(archive);
        return archiveId;
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        internal void Advance(TimeSpan duration) => _now += duration;
    }
}
