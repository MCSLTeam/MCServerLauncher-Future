using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using MCServerLauncher.Daemon.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MCServerLauncher.Daemon.ApplicationCore.Audit;

/// <summary>
/// Daemon-owned bounded rolling JSONL history: sequential segment files, byte/age retention, and
/// crash recovery that truncates only an incomplete final record. Appends never throw into the
/// caller's mutation path; a failed write is counted and surfaced through <see cref="DroppedRecords" />.
/// </summary>
internal sealed class BoundedJsonlLog<T>
    where T : class
{
    private readonly object _gate = new();
    private readonly string _root;
    private readonly string _prefix;
    private readonly JsonTypeInfo<T> _typeInfo;
    private readonly Func<T, DateTimeOffset> _timestamp;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly long _segmentBytes;
    private readonly long _maximumBytes;
    private readonly TimeSpan _retention;
    private long _droppedRecords;
    private int _currentSegment;
    private long _currentSegmentBytes;

    internal BoundedJsonlLog(
        string rootDirectory,
        string segmentPrefix,
        JsonTypeInfo<T> typeInfo,
        Func<T, DateTimeOffset> timestamp,
        long maximumBytes,
        TimeSpan retention,
        TimeProvider? timeProvider = null,
        ILogger? logger = null,
        long segmentBytes = 4 * 1024 * 1024)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segmentPrefix);
        ArgumentNullException.ThrowIfNull(typeInfo);
        ArgumentNullException.ThrowIfNull(timestamp);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumBytes, 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(retention, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(segmentBytes, 1024);
        _root = Path.GetFullPath(rootDirectory);
        _prefix = segmentPrefix;
        _typeInfo = typeInfo;
        _timestamp = timestamp;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger.Instance;
        _segmentBytes = segmentBytes;
        _maximumBytes = maximumBytes;
        _retention = retention;
        Directory.CreateDirectory(_root);
        Load();
    }

    /// <summary>
    /// Appends dropped by write failures instead of failing the caller's mutation (fail-open by
    /// design); non-zero means the history has an observable hole.
    /// </summary>
    internal long DroppedRecords => Interlocked.Read(ref _droppedRecords);

    /// <summary>
    /// Appends one record. Never throws: history is an observer of mutations, not a participant.
    /// </summary>
    internal void Append(T record)
    {
        ArgumentNullException.ThrowIfNull(record);
        try
        {
            var line = JsonSerializer.SerializeToUtf8Bytes(record, _typeInfo);
            lock (_gate)
            {
                if (_currentSegmentBytes + line.Length + 1 > _segmentBytes && _currentSegmentBytes > 0)
                {
                    _currentSegment++;
                    _currentSegmentBytes = 0;
                }

                var path = SegmentPath(_currentSegment);
                try
                {
                    using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
                    stream.Write(line);
                    stream.WriteByte((byte)'\n');
                }
                catch
                {
                    // A write that fails partway leaves an unterminated line. The next append would
                    // otherwise land on the same physical line and silently destroy that later,
                    // acknowledged record, so roll the segment back to the last complete one.
                    TruncateToLocked(path, _currentSegmentBytes);
                    throw;
                }

                _currentSegmentBytes += line.Length + 1;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Interlocked.Increment(ref _droppedRecords);
            _logger.LogWarning(
                exception,
                "[BoundedJsonlLog] Dropped a '{Prefix}' history record after a write failure.",
                _prefix);
            return;
        }

        // Retention runs after the record is durably written and outside the drop accounting: a
        // retention failure loses no record, so counting it as a dropped record would report a
        // hole in a history that has none.
        try
        {
            lock (_gate)
            {
                EnforceRetentionLocked();
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(
                exception,
                "[BoundedJsonlLog] Failed to enforce '{Prefix}' history retention.",
                _prefix);
        }
    }

    private void TruncateToLocked(string path, long length)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
            if (stream.Length > length)
                stream.SetLength(length);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(
                exception,
                "[BoundedJsonlLog] Failed to roll back a torn '{Prefix}' append.",
                _prefix);
        }
    }

    /// <summary>
    /// Newest-first bounded read. <paramref name="maximumRecords" /> caps the result; the optional
    /// window and filter are applied per record before the cap.
    /// </summary>
    internal IReadOnlyList<T> ReadNewestFirst(
        int maximumRecords,
        DateTimeOffset? notBefore = null,
        DateTimeOffset? notAfter = null,
        Func<T, bool>? filter = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumRecords, 1);
        var results = new List<T>(Math.Min(maximumRecords, 256));
        // Segments are append-only, so reading them outside the gate is safe and keeps a long
        // query from stalling every mutation waiting to append.
        var segments = SnapshotSegments();
        for (var index = segments.Count - 1; index >= 0 && results.Count < maximumRecords; index--)
        {
            var path = SegmentPath(segments[index]);
            if (!File.Exists(path))
                continue;

            var records = ReadSegment(path);
            for (var position = records.Count - 1; position >= 0 && results.Count < maximumRecords; position--)
            {
                var record = records[position];
                var at = _timestamp(record);
                if (notBefore is { } floor && at < floor)
                    continue;
                if (notAfter is { } ceiling && at > ceiling)
                    continue;
                if (filter is not null && !filter(record))
                    continue;
                results.Add(record);
            }
        }

        return results;
    }

    /// <summary>
    /// Oldest-first read across the window, capped. When the window holds more than the cap the
    /// OLDEST records are dropped, so a truncated range still ends at the newest data the caller
    /// asked for instead of stopping short of it.
    /// </summary>
    internal IReadOnlyList<T> ReadRange(
        DateTimeOffset notBefore,
        DateTimeOffset notAfter,
        int maximumRecords)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumRecords, 1);
        var window = new Queue<T>();
        var segments = SnapshotSegments();
        foreach (var segment in segments)
        {
            var path = SegmentPath(segment);
            if (!File.Exists(path))
                continue;

            foreach (var record in ReadSegment(path))
            {
                var at = _timestamp(record);
                if (at < notBefore || at > notAfter)
                    continue;
                window.Enqueue(record);
                if (window.Count > maximumRecords)
                    window.Dequeue();
            }
        }

        return [.. window];
    }

    private List<int> SnapshotSegments()
    {
        lock (_gate)
        {
            var segments = ListSegments();
            if (!segments.Contains(_currentSegment))
                segments.Add(_currentSegment);
            return segments;
        }
    }

    private void Load()
    {
        var segments = ListSegments();
        if (segments.Count == 0)
        {
            _currentSegment = 0;
            _currentSegmentBytes = 0;
            return;
        }

        _currentSegment = segments[^1];
        // Crash recovery: the spec permits truncating only an incomplete final JSONL record.
        var tailPath = SegmentPath(_currentSegment);
        var recovered = RecoverSegmentTail(tailPath);
        _currentSegmentBytes = recovered;
        try
        {
            lock (_gate)
            {
                EnforceRetentionLocked();
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Startup retention is best-effort: the log opens fail-open rather than failing daemon
            // composition, and the next append retries.
            _logger.LogWarning(
                exception,
                "[BoundedJsonlLog] Failed to enforce '{Prefix}' history retention at startup.",
                _prefix);
        }
    }

    private long RecoverSegmentTail(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length == 0)
                return 0;

            var validLength = 0;
            var lineStart = 0;
            for (var index = 0; index < bytes.Length; index++)
            {
                if (bytes[index] != (byte)'\n')
                    continue;

                var line = bytes.AsSpan(lineStart, index - lineStart);
                if (IsValidRecord(line))
                {
                    validLength = index + 1;
                    lineStart = index + 1;
                    continue;
                }

                // A bad record that still has more data after it is interior corruption, not a
                // torn tail: the segment is otherwise intact, so leave every byte in place and let
                // the read path skip the one unparseable line instead of destroying everything that
                // follows it.
                if (index + 1 < bytes.Length)
                    return bytes.Length;

                // The bad record is the last thing in the file and is newline-terminated; treat it
                // the same as an unterminated tail below - truncate it away.
                break;
            }

            if (validLength != bytes.Length)
            {
                _logger.LogWarning(
                    "[BoundedJsonlLog] Truncating '{Path}' from {Original} to {Recovered} bytes after crash recovery.",
                    path,
                    bytes.Length,
                    validLength);
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
                stream.SetLength(validLength);
            }

            return validLength;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "[BoundedJsonlLog] Failed to recover '{Path}'.", path);
            return 0;
        }
    }

    private bool IsValidRecord(ReadOnlySpan<byte> line)
    {
        if (line.Length == 0)
            return false;
        try
        {
            return JsonSerializer.Deserialize(line, _typeInfo) is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private List<T> ReadSegment(string path)
    {
        var records = new List<T>();
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            while (reader.ReadLine() is { } line)
            {
                if (line.Length == 0)
                    continue;
                try
                {
                    if (JsonSerializer.Deserialize(line, _typeInfo) is { } record)
                        records.Add(record);
                }
                catch (JsonException)
                {
                    // A torn tail is truncated at load; anything else unreadable is skipped rather
                    // than failing the whole bounded query.
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "[BoundedJsonlLog] Failed to read segment '{Path}'.", path);
        }

        return records;
    }

    private void EnforceRetentionLocked()
    {
        var segments = ListSegments();
        if (segments.Count == 0)
            return;

        var ageFloor = _timeProvider.GetUtcNow() - _retention;
        long totalBytes = 0;
        var sizes = new Dictionary<int, long>(segments.Count);
        foreach (var segment in segments)
        {
            var length = new FileInfo(SegmentPath(segment)).Length;
            sizes[segment] = length;
            totalBytes += length;
        }

        foreach (var segment in segments)
        {
            // The active segment never retires: age is judged by last write, and the byte cap must
            // keep at least the newest data.
            if (segment == _currentSegment)
                break;

            var path = SegmentPath(segment);
            var overByteCap = totalBytes > _maximumBytes;
            var overAge = File.GetLastWriteTimeUtc(path) < ageFloor.UtcDateTime;
            if (!overByteCap && !overAge)
                break;

            FileManager.DeleteIfExists(path);
            totalBytes -= sizes[segment];
        }
    }

    private List<int> ListSegments()
    {
        var segments = new List<int>();
        try
        {
            foreach (var path in Directory.EnumerateFiles(_root, $"{_prefix}-*.jsonl"))
            {
                var name = Path.GetFileNameWithoutExtension(path);
                if (int.TryParse(name.AsSpan(_prefix.Length + 1), out var number))
                    segments.Add(number);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // An unreadable log directory must not fail a bounded read or a retention pass; both
            // degrade to what they can still see.
            _logger.LogWarning(exception, "[BoundedJsonlLog] Failed to enumerate '{Prefix}' segments.", _prefix);
            return [];
        }

        segments.Sort();
        return segments;
    }

    private string SegmentPath(int segment) => Path.Combine(_root, $"{_prefix}-{segment:D6}.jsonl");
}
