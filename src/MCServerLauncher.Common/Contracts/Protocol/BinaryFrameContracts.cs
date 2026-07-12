using System.Buffers.Binary;

namespace MCServerLauncher.Common.Contracts.Protocol;

public enum BinaryFrameKind : byte
{
    UploadChunk = 1,
    DownloadChunk = 2
}

public sealed record BinaryFrameHeader
{
    public BinaryFrameHeader(BinaryFrameKind kind, Guid sessionId, long offset, uint payloadLength)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), "A binary frame kind must be defined.");
        }

        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "A binary frame offset cannot be negative.");
        }

        if (sessionId == Guid.Empty)
        {
            throw new ArgumentException("A binary frame session identifier cannot be empty.", nameof(sessionId));
        }

        Kind = kind;
        SessionId = sessionId;
        Offset = offset;
        PayloadLength = payloadLength;
    }

    public byte Version => BinaryFrameCodec.CurrentVersion;

    public BinaryFrameKind Kind { get; }

    public Guid SessionId { get; }

    public long Offset { get; }

    public uint PayloadLength { get; }
}

public enum BinaryFrameReadError
{
    None,
    FrameTooShort,
    UnsupportedVersion,
    UnknownKind,
    ReservedNotZero,
    EmptySessionId,
    NegativeOffset,
    PayloadLengthMismatch,
    PayloadTooLarge
}

public readonly record struct BinaryFrameTrustedTarget(BinaryFrameKind Kind, Guid SessionId);

public sealed record BinaryFrameReadResult
{
    internal BinaryFrameReadResult(
        BinaryFrameReadError error,
        BinaryFrameHeader? header,
        BinaryFrameTrustedTarget? trustedTarget,
        long? offset,
        uint? declaredPayloadLength,
        uint? actualPayloadLength)
    {
        Error = error;
        Header = header;
        TrustedTarget = trustedTarget;
        Offset = offset;
        DeclaredPayloadLength = declaredPayloadLength;
        ActualPayloadLength = actualPayloadLength;
    }

    public BinaryFrameReadError Error { get; }
    public BinaryFrameHeader? Header { get; }
    public BinaryFrameTrustedTarget? TrustedTarget { get; }
    public long? Offset { get; }
    public uint? DeclaredPayloadLength { get; }
    public uint? ActualPayloadLength { get; }
    public bool IsSuccess => Error == BinaryFrameReadError.None;
}

public enum BinaryFrameWriteError
{
    None,
    DestinationLengthMismatch,
    PayloadLengthMismatch,
    PayloadTooLarge
}

public static class BinaryFrameCodec
{
    public const int HeaderSize = 32;
    public const byte CurrentVersion = 1;
    public const uint DefaultMaximumChunkSize = 1024 * 1024;

    public static bool TryRead(
        ReadOnlySpan<byte> frame,
        out BinaryFrameHeader? header,
        out BinaryFrameReadError error,
        uint maximumChunkSize = DefaultMaximumChunkSize)
    {
        var success = TryRead(frame, out var result, maximumChunkSize);
        header = result.Header;
        error = result.Error;
        return success;
    }

    public static bool TryRead(
        ReadOnlySpan<byte> frame,
        out BinaryFrameReadResult result,
        uint maximumChunkSize = DefaultMaximumChunkSize)
    {
        if (frame.Length < HeaderSize)
        {
            result = Failure(BinaryFrameReadError.FrameTooShort);
            return false;
        }

        if (frame[0] != CurrentVersion)
        {
            result = Failure(BinaryFrameReadError.UnsupportedVersion);
            return false;
        }

        var kind = (BinaryFrameKind)frame[1];
        if (!Enum.IsDefined(kind))
        {
            result = Failure(BinaryFrameReadError.UnknownKind);
            return false;
        }

        if (frame[2] != 0 || frame[3] != 0)
        {
            result = Failure(BinaryFrameReadError.ReservedNotZero);
            return false;
        }

        var sessionId = new Guid(frame.Slice(4, 16), bigEndian: true);
        if (sessionId == Guid.Empty)
        {
            result = Failure(BinaryFrameReadError.EmptySessionId);
            return false;
        }

        var target = new BinaryFrameTrustedTarget(kind, sessionId);

        var offset = BinaryPrimitives.ReadInt64LittleEndian(frame.Slice(20, sizeof(long)));
        if (offset < 0)
        {
            result = Failure(BinaryFrameReadError.NegativeOffset, target, offset);
            return false;
        }

        var payloadLength = BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(28, sizeof(uint)));
        var actualPayloadLength = checked((uint)(frame.Length - HeaderSize));
        if (payloadLength != actualPayloadLength)
        {
            result = Failure(BinaryFrameReadError.PayloadLengthMismatch, target, offset, payloadLength, actualPayloadLength);
            return false;
        }

        if (payloadLength > maximumChunkSize)
        {
            result = Failure(BinaryFrameReadError.PayloadTooLarge, target, offset, payloadLength, actualPayloadLength);
            return false;
        }

        var header = new BinaryFrameHeader(
            kind,
            sessionId,
            offset,
            payloadLength);
        result = new BinaryFrameReadResult(
            BinaryFrameReadError.None, header, target, offset, payloadLength, actualPayloadLength);
        return true;
    }

    public static bool TryWrite(
        Span<byte> destination,
        BinaryFrameHeader header,
        ReadOnlySpan<byte> payload,
        out BinaryFrameWriteError error,
        uint maximumChunkSize = DefaultMaximumChunkSize)
    {
        ArgumentNullException.ThrowIfNull(header);
        if (header.SessionId == Guid.Empty)
            throw new ArgumentException("A binary frame session identifier cannot be empty.", nameof(header));

        var payloadLength = checked((uint)payload.Length);
        if (header.PayloadLength != payloadLength)
        {
            error = BinaryFrameWriteError.PayloadLengthMismatch;
            return false;
        }

        if (payloadLength > maximumChunkSize)
        {
            error = BinaryFrameWriteError.PayloadTooLarge;
            return false;
        }

        if (destination.Length != checked(HeaderSize + payload.Length))
        {
            error = BinaryFrameWriteError.DestinationLengthMismatch;
            return false;
        }

        destination[0] = CurrentVersion;
        destination[1] = (byte)header.Kind;
        destination[2] = 0;
        destination[3] = 0;
        if (!header.SessionId.TryWriteBytes(destination.Slice(4, 16), bigEndian: true, out var bytesWritten) ||
            bytesWritten != 16)
        {
            throw new InvalidOperationException("A GUID did not fit in the fixed binary frame header.");
        }

        BinaryPrimitives.WriteInt64LittleEndian(destination.Slice(20, sizeof(long)), header.Offset);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(28, sizeof(uint)), payloadLength);
        payload.CopyTo(destination[HeaderSize..]);
        error = BinaryFrameWriteError.None;
        return true;
    }

    private static BinaryFrameReadResult Failure(
        BinaryFrameReadError error,
        BinaryFrameTrustedTarget? target = null,
        long? offset = null,
        uint? declaredPayloadLength = null,
        uint? actualPayloadLength = null) =>
        new(error, null, target, offset, declaredPayloadLength, actualPayloadLength);
}
