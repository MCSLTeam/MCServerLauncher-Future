using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using MCServerLauncher.Common.Contracts.Protocol;
using MCServerLauncher.Common.Contracts.Serialization;
using MCServerLauncher.Daemon.Remote.Rpc.Transport;

namespace MCServerLauncher.Benchmarks.Benchmarks;

[MemoryDiagnoser]
public class V2BinaryPipelineBenchmarks
{
    private static readonly Guid SessionId = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
    private readonly byte[] _payload = new byte[64 * 1024];
    private byte[] _frame = null!;

    [GlobalSetup]
    public void Setup()
    {
        _frame = new byte[BinaryFrameCodec.HeaderSize + _payload.Length];
        BinaryFrameCodec.TryWrite(
            _frame,
            new BinaryFrameHeader(BinaryFrameKind.UploadChunk, SessionId, 0, checked((uint)_payload.Length)),
            _payload,
            out _);
    }

    [Benchmark]
    public BinaryFrameReadResult ParseUpload64KiB()
    {
        BinaryFrameCodec.TryRead(_frame, out BinaryFrameReadResult result);
        return result;
    }

    [Benchmark]
    public byte[] WriteDownload64KiB()
    {
        var destination = new byte[_frame.Length];
        BinaryFrameCodec.TryWrite(
            destination,
            new BinaryFrameHeader(BinaryFrameKind.DownloadChunk, SessionId, 0, checked((uint)_payload.Length)),
            _payload,
            out _);
        return destination;
    }

    [Benchmark]
    public byte[] SerializeAcceptedUploadAcknowledgement() =>
        JsonSerializer.SerializeToUtf8Bytes(
            new JsonRpcUploadAcknowledgementNotification(
                new UploadChunkAcknowledgement(
                    SessionId, 0, _payload.Length, UploadChunkAcknowledgementStatus.Accepted, null)),
            BuiltInProtocolJsonContext.Default.JsonRpcUploadAcknowledgementNotification);

    [Benchmark]
    public object BuildDownloadGroup()
    {
        var binary = new byte[_frame.Length];
        BinaryFrameCodec.TryWrite(
            binary,
            new BinaryFrameHeader(BinaryFrameKind.DownloadChunk, SessionId, 0, checked((uint)_payload.Length)),
            _payload,
            out _);
        return V2OutboundMessage.TextThenBinary(
            V2OutboundFrame.Text(ImmutableArray.Create("{}"u8.ToArray())),
            V2OutboundFrame.Binary(ImmutableCollectionsMarshal.AsImmutableArray(binary)));
    }
}
