using System.Security.Cryptography;
using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using MCServerLauncher.DaemonClient.WebSocketPlugin;

namespace MCServerLauncher.Benchmarks.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
[JsonExporterAttribute.Full]
public class LegacyBinaryUploadMigrationBenchmarks
{
    private const int LegacyV1FileIdLength = 16;
    private const int LegacyV1OffsetLength = 8;
    private const int LegacyV1ChecksumLength = 20;
    private const int LegacyV1HeaderLength =
        LegacyV1FileIdLength + LegacyV1OffsetLength + LegacyV1ChecksumLength;
    private const long FixedOffset = 0x0102030405060708;

    private static readonly Guid FixedFileId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    private byte[] _acknowledgement = null!;
    private byte[] _chunk = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _chunk = new byte[64 * 1024];
        for (var i = 0; i < _chunk.Length; i++)
            _chunk[i] = (byte)(i % 251);

        _acknowledgement = Encoding.UTF8.GetBytes(
            $$"""
              {
                "file_id": "{{FixedFileId}}",
                "done": false,
                "received": {{_chunk.Length}}
              }
              """);

        var frame = BuildLegacyV1RawBinaryUploadFrameMigrationBaseline();
        if (frame.Length != LegacyV1HeaderLength + _chunk.Length)
            throw new InvalidOperationException("Legacy V1 raw-binary migration frame precheck produced an unexpected length.");
        if (new Guid(frame.AsSpan(0, LegacyV1FileIdLength)) != FixedFileId)
            throw new InvalidOperationException("Legacy V1 raw-binary migration frame precheck produced an unexpected file id.");
        if (BitConverter.ToInt64(frame, LegacyV1FileIdLength) != FixedOffset)
            throw new InvalidOperationException("Legacy V1 raw-binary migration frame precheck produced an unexpected offset.");

        var parsed = WsReceivedPlugin.ParseInboundEnvelopeFromBytes(_acknowledgement);
        if (parsed.BinaryUploadResponse is not { } response || response.FileId != FixedFileId)
            throw new InvalidOperationException("Legacy V1 raw-binary acknowledgement precheck failed.");
    }

    [Benchmark]
    public byte[] BuildLegacyV1RawBinaryUploadFrameMigrationBaseline()
    {
        // Migration baseline only: this deliberately mirrors the retiring V1 wire layout.
        var checksum = SHA1.HashData(_chunk);
        var payload = new byte[LegacyV1HeaderLength + _chunk.Length];

        FixedFileId.TryWriteBytes(payload.AsSpan(0, LegacyV1FileIdLength));
        BitConverter.TryWriteBytes(payload.AsSpan(LegacyV1FileIdLength, LegacyV1OffsetLength), FixedOffset);
        Array.Copy(checksum, 0, payload, LegacyV1FileIdLength + LegacyV1OffsetLength, LegacyV1ChecksumLength);
        Array.Copy(_chunk, 0, payload, LegacyV1HeaderLength, _chunk.Length);

        return payload;
    }

    [Benchmark]
    public long ParseLegacyV1RawBinaryUploadAcknowledgementMigrationBaseline()
    {
        var parsed = WsReceivedPlugin.ParseInboundEnvelopeFromBytes(_acknowledgement);
        var response = parsed.BinaryUploadResponse
                       ?? throw new InvalidOperationException("Legacy V1 raw-binary acknowledgement was not recognized.");
        return response.Received;
    }
}
