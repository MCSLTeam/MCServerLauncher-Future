using System.Buffers.Binary;
using MCServerLauncher.Common.Contracts.Protocol;

namespace MCServerLauncher.ProtocolTests.Rpc.Wire;

public sealed class BinaryFrameCodecTests
{
    private static readonly Guid SessionId = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
    private static readonly byte[] Payload = [0xaa, 0xbb, 0xcc];

    [Fact]
    public void GoldenFrameUsesTheFrozenLayoutAndEndianRules()
    {
        var header = new BinaryFrameHeader(
            BinaryFrameKind.UploadChunk,
            SessionId,
            0x0102030405060708,
            checked((uint)Payload.Length));
        var frame = new byte[BinaryFrameCodec.HeaderSize + Payload.Length];

        Assert.True(BinaryFrameCodec.TryWrite(frame, header, Payload, out var error));
        Assert.Equal(BinaryFrameWriteError.None, error);
        Assert.Equal(
            new byte[]
            {
                0x01, 0x01, 0x00, 0x00,
                0x00, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77,
                0x88, 0x99, 0xaa, 0xbb, 0xcc, 0xdd, 0xee, 0xff,
                0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01,
                0x03, 0x00, 0x00, 0x00,
                0xaa, 0xbb, 0xcc
            },
            frame);
    }

    [Fact]
    public void EveryDefinedKindRoundTripsWithoutStructLayoutOrHostEndianness()
    {
        foreach (var kind in Enum.GetValues<BinaryFrameKind>())
        {
            var expected = new BinaryFrameHeader(kind, SessionId, 42, checked((uint)Payload.Length));
            var frame = new byte[BinaryFrameCodec.HeaderSize + Payload.Length];

            Assert.True(BinaryFrameCodec.TryWrite(frame, expected, Payload, out var writeError));
            Assert.Equal(BinaryFrameWriteError.None, writeError);
            Assert.True(BinaryFrameCodec.TryRead(frame, out var actual, out var readError));
            Assert.Equal(BinaryFrameReadError.None, readError);
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void ReaderClassifiesEveryMalformedHeaderAndLengthCase()
    {
        var valid = CreateValidFrame();

        AssertReadError(valid[..^4], BinaryFrameReadError.FrameTooShort);

        var version = (byte[])valid.Clone();
        version[0] = 2;
        AssertReadError(version, BinaryFrameReadError.UnsupportedVersion);

        var kind = (byte[])valid.Clone();
        kind[1] = byte.MaxValue;
        AssertReadError(kind, BinaryFrameReadError.UnknownKind);

        var reserved = (byte[])valid.Clone();
        reserved[2] = 1;
        AssertReadError(reserved, BinaryFrameReadError.ReservedNotZero);

        var negativeOffset = (byte[])valid.Clone();
        BinaryPrimitives.WriteInt64LittleEndian(negativeOffset.AsSpan(20, sizeof(long)), -1);
        AssertReadError(negativeOffset, BinaryFrameReadError.NegativeOffset);

        var mismatchedLength = (byte[])valid.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(mismatchedLength.AsSpan(28, sizeof(uint)), 2);
        AssertReadError(mismatchedLength, BinaryFrameReadError.PayloadLengthMismatch);

        Assert.False(BinaryFrameCodec.TryRead(valid, out _, out var tooLarge, maximumChunkSize: 2));
        Assert.Equal(BinaryFrameReadError.PayloadTooLarge, tooLarge);
    }

    [Fact]
    public void WriterRejectsLengthMismatchShortDestinationAndConfiguredMaximum()
    {
        var wrongLength = new BinaryFrameHeader(BinaryFrameKind.DownloadChunk, SessionId, 0, 2);
        var validHeader = new BinaryFrameHeader(
            BinaryFrameKind.DownloadChunk,
            SessionId,
            0,
            checked((uint)Payload.Length));

        Assert.False(BinaryFrameCodec.TryWrite(
            new byte[BinaryFrameCodec.HeaderSize + Payload.Length],
            wrongLength,
            Payload,
            out var mismatch));
        Assert.Equal(BinaryFrameWriteError.PayloadLengthMismatch, mismatch);

        Assert.False(BinaryFrameCodec.TryWrite(
            new byte[BinaryFrameCodec.HeaderSize],
            validHeader,
            Payload,
            out var shortDestination));
        Assert.Equal(BinaryFrameWriteError.DestinationLengthMismatch, shortDestination);

        Assert.False(BinaryFrameCodec.TryWrite(
            new byte[BinaryFrameCodec.HeaderSize + Payload.Length],
            validHeader,
            Payload,
            out var tooLarge,
            maximumChunkSize: 2));
        Assert.Equal(BinaryFrameWriteError.PayloadTooLarge, tooLarge);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new BinaryFrameHeader(BinaryFrameKind.UploadChunk, SessionId, -1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new BinaryFrameHeader((BinaryFrameKind)byte.MaxValue, SessionId, 0, 0));
    }

    private static byte[] CreateValidFrame()
    {
        var frame = new byte[BinaryFrameCodec.HeaderSize + Payload.Length];
        var header = new BinaryFrameHeader(
            BinaryFrameKind.UploadChunk,
            SessionId,
            0,
            checked((uint)Payload.Length));
        Assert.True(BinaryFrameCodec.TryWrite(frame, header, Payload, out var error));
        Assert.Equal(BinaryFrameWriteError.None, error);
        return frame;
    }

    private static void AssertReadError(ReadOnlySpan<byte> frame, BinaryFrameReadError expected)
    {
        Assert.False(BinaryFrameCodec.TryRead(frame, out var header, out var actual));
        Assert.Null(header);
        Assert.Equal(expected, actual);
    }
}
