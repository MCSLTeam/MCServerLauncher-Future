using MCServerLauncher.Common.Contracts.Audit;
using MCServerLauncher.Common.Contracts.Serialization;
using MCServerLauncher.Daemon.ApplicationCore.Audit;

namespace MCServerLauncher.ProtocolTests;

public sealed class AuditLogTests
{
    [Fact]
    public void Record_RoundTripsNewestFirstWithCapAndFilters()
    {
        var root = Directory.CreateTempSubdirectory("mcsl-audit-log-").FullName;
        var time = new ManualTimeProvider(DateTimeOffset.Parse("2026-07-27T00:00:00+00:00"));
        var log = new AuditLog(new DaemonAuditConfig(), time, root);

        for (var index = 0; index < 5; index++)
        {
            log.Record(Event("user-a", "mcsl.instance.start", target: $"target-{index}"));
            time.Advance(TimeSpan.FromSeconds(1));
        }

        log.Record(Event("user-a", "mcsl.instance.stop", target: "target-0"));

        var all = log.Query(new AuditQuery(OwnerPrincipal: "*"));
        Assert.Equal(6, all.Records.Length);
        Assert.Equal("mcsl.instance.stop", all.Records[0].Method);
        Assert.Equal("target-4", all.Records[1].Target);
        Assert.Equal(0, all.DroppedRecords);

        var capped = log.Query(new AuditQuery(MaximumRecords: 2, OwnerPrincipal: "*"));
        Assert.Equal(2, capped.Records.Length);
        Assert.Equal("mcsl.instance.stop", capped.Records[0].Method);

        var filtered = log.Query(new AuditQuery(
            Method: "mcsl.instance.start",
            Target: "target-2",
            OwnerPrincipal: "*"));
        var match = Assert.Single(filtered.Records);
        Assert.Equal("target-2", match.Target);

        var window = log.Query(new AuditQuery(
            NotBefore: DateTimeOffset.Parse("2026-07-27T00:00:01+00:00"),
            NotAfter: DateTimeOffset.Parse("2026-07-27T00:00:02+00:00"),
            OwnerPrincipal: "*"));
        Assert.Equal(2, window.Records.Length);
    }

    [Fact]
    public void Query_ScopesByOwnerPrincipal()
    {
        var root = Directory.CreateTempSubdirectory("mcsl-audit-scope-").FullName;
        var log = new AuditLog(new DaemonAuditConfig(), rootDirectory: root);
        log.Record(Event("user-a", "mcsl.instance.start"));
        log.Record(Event("user-b", "mcsl.instance.stop"));

        Assert.Equal(2, log.Query(new AuditQuery(OwnerPrincipal: "*")).Records.Length);
        var scoped = Assert.Single(log.Query(new AuditQuery(OwnerPrincipal: "user-a")).Records);
        Assert.Equal("user-a", scoped.Principal);
        // A scoped caller cannot widen its view through the principal filter.
        Assert.Empty(log.Query(new AuditQuery(Principal: "user-b", OwnerPrincipal: "user-a")).Records);
        // An unbound query returns nothing rather than everything.
        Assert.Empty(log.Query(new AuditQuery()).Records);
    }

    [Fact]
    public void Load_TruncatesOnlyTheTornTailAndKeepsRecording()
    {
        var root = Directory.CreateTempSubdirectory("mcsl-audit-torn-").FullName;
        var log = new AuditLog(new DaemonAuditConfig(), rootDirectory: root);
        log.Record(Event("user-a", "mcsl.instance.start"));
        log.Record(Event("user-a", "mcsl.instance.stop"));

        var segment = Directory.EnumerateFiles(root, "audit-*.jsonl").Single();
        File.AppendAllText(segment, "{\"timestamp\":\"2026-07-27T00:0");

        var reloaded = new AuditLog(new DaemonAuditConfig(), rootDirectory: root);
        Assert.Equal(2, reloaded.Query(new AuditQuery(OwnerPrincipal: "*")).Records.Length);
        reloaded.Record(Event("user-a", "mcsl.backup.create"));
        var records = reloaded.Query(new AuditQuery(OwnerPrincipal: "*")).Records;
        Assert.Equal(3, records.Length);
        Assert.Equal("mcsl.backup.create", records[0].Method);
    }

    [Fact]
    public void Append_WriteFailureIsCountedInsteadOfThrown()
    {
        var root = Directory.CreateTempSubdirectory("mcsl-audit-drop-").FullName;
        var log = new AuditLog(new DaemonAuditConfig(), rootDirectory: root);
        log.Record(Event("user-a", "mcsl.instance.start"));

        // Replace the whole log directory with a plain file so the next append cannot land.
        Directory.Delete(root, recursive: true);
        File.WriteAllText(root, "not a directory");
        try
        {
            log.Record(Event("user-a", "mcsl.instance.stop"));
            Assert.Equal(1, log.DroppedRecords);
            Assert.Equal(1, log.Query(new AuditQuery(OwnerPrincipal: "*")).DroppedRecords);
        }
        finally
        {
            File.Delete(root);
        }
    }

    [Fact]
    public void Retention_RetiresOldSegmentsByByteCapButNeverTheActiveOne()
    {
        var root = Directory.CreateTempSubdirectory("mcsl-audit-cap-").FullName;
        var log = new BoundedJsonlLog<AuditRecord>(
            root,
            "audit",
            ApplicationContractJsonContext.Default.AuditRecord,
            static record => record.Timestamp,
            maximumBytes: 2048,
            retention: TimeSpan.FromDays(30),
            segmentBytes: 1024);

        for (var index = 0; index < 40; index++)
        {
            log.Append(Record("user-a", "mcsl.instance.start", $"target-{index:D3}"));
        }

        var segments = Directory.EnumerateFiles(root, "audit-*.jsonl").Order(StringComparer.Ordinal).ToArray();
        Assert.True(segments.Length >= 2, "expected the log to roll into multiple segments");
        var totalBytes = segments.Sum(path => new FileInfo(path).Length);
        Assert.True(totalBytes <= 2048 + 1024, $"retention left {totalBytes} bytes");
        // The oldest segment must be gone and the newest record must survive.
        Assert.DoesNotContain(segments, path => path.EndsWith("audit-000000.jsonl", StringComparison.Ordinal));
        var newest = log.ReadNewestFirst(1);
        Assert.Equal("target-039", Assert.Single(newest).Target);
    }

    [Fact]
    public void Retention_RetiresSegmentsOlderThanTheAgeFloor()
    {
        var root = Directory.CreateTempSubdirectory("mcsl-audit-age-").FullName;
        var time = new ManualTimeProvider(DateTimeOffset.Parse("2026-07-01T00:00:00+00:00"));
        var log = new BoundedJsonlLog<AuditRecord>(
            root,
            "audit",
            ApplicationContractJsonContext.Default.AuditRecord,
            static record => record.Timestamp,
            maximumBytes: 64 * 1024 * 1024,
            retention: TimeSpan.FromDays(7),
            timeProvider: time,
            segmentBytes: 1024);

        for (var index = 0; index < 10; index++)
        {
            log.Append(Record("user-a", "mcsl.instance.start", $"old-{index}"));
        }

        var segmentsBefore = Directory.EnumerateFiles(root, "audit-*.jsonl").Order(StringComparer.Ordinal).ToArray();
        Assert.True(segmentsBefore.Length >= 2);
        // Segment age is judged by the file write stamp, so back-date every retired candidate.
        foreach (var path in segmentsBefore)
        {
            File.SetLastWriteTimeUtc(path, new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        }

        time.Advance(TimeSpan.FromDays(8));
        log.Append(Record("user-a", "mcsl.instance.start", "fresh"));

        // Every non-active segment predates the age floor and retires; the active one survives.
        var segmentsAfter = Directory.EnumerateFiles(root, "audit-*.jsonl").Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(segmentsBefore[^1], Assert.Single(segmentsAfter));
        var readable = log.ReadNewestFirst(100);
        Assert.Equal("fresh", readable[0].Target);
    }

    [Fact]
    public void ReadRange_ReturnsOldestFirstWithinTheWindow()
    {
        var root = Directory.CreateTempSubdirectory("mcsl-audit-range-").FullName;
        var log = new BoundedJsonlLog<AuditRecord>(
            root,
            "audit",
            ApplicationContractJsonContext.Default.AuditRecord,
            static record => record.Timestamp,
            maximumBytes: 64 * 1024 * 1024,
            retention: TimeSpan.FromDays(30));

        var start = DateTimeOffset.Parse("2026-07-27T00:00:00+00:00");
        for (var index = 0; index < 5; index++)
        {
            log.Append(Record("user-a", "mcsl.instance.start", $"target-{index}", start.AddMinutes(index)));
        }

        var range = log.ReadRange(start.AddMinutes(1), start.AddMinutes(3), maximumRecords: 10);
        Assert.Equal(["target-1", "target-2", "target-3"], range.Select(record => record.Target));
        var capped = log.ReadRange(start, start.AddMinutes(10), maximumRecords: 2);
        Assert.Equal(["target-0", "target-1"], capped.Select(record => record.Target));
    }

    private static AuditEvent Event(string principal, string method, string? target = null) =>
        new(principal, null, method, method, target, null, null, null, true, null, null);

    private static AuditRecord Record(
        string principal,
        string method,
        string? target,
        DateTimeOffset? timestamp = null) =>
        new(
            timestamp ?? DateTimeOffset.Parse("2026-07-27T00:00:00+00:00"),
            principal,
            null,
            method,
            method,
            target,
            null,
            null,
            null,
            true,
            null,
            null);

    private sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        internal void Advance(TimeSpan delta) => _now += delta;
    }
}
