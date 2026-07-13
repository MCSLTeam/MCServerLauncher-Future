using System;
using System.Buffers;
using System.IO;
using MCServerLauncher.Common.Contracts.Protocol;
using TouchSocket.Http.WebSockets;

namespace MCServerLauncher.DaemonClient.Connection.V2;

internal sealed class TouchSocketV2MessageAssembler
{
    internal const int MaxTextMessageSize = 10 * 1024 * 1024;
    internal const int MaxBinaryMessageSize = BinaryFrameCodec.HeaderSize +
        (int)BinaryFrameCodec.DefaultMaximumChunkSize;

    private readonly object _gate = new();
    private readonly WebSocketMessageCombinator _combinator = new();
    private bool _combining;
    private int _payloadLength;
    private WSDataType _initialOpcode;

    internal bool TryAssemble(WSDataFrame frame, out WebSocketMessage message)
    {
        ArgumentNullException.ThrowIfNull(frame);
        lock (_gate)
            return TryAssembleLocked(frame, out message);
    }

    internal void Clear()
    {
        lock (_gate)
            Reset();
    }

    private bool TryAssembleLocked(WSDataFrame frame, out WebSocketMessage message)
    {
        try
        {
            switch (frame.Opcode)
            {
                case WSDataType.Text:
                case WSDataType.Binary:
                    if (_combining)
                    {
                        throw new InvalidDataException(
                            "A fragmented V2 message cannot be interleaved with another data message.");
                    }

                    _initialOpcode = frame.Opcode;
                    CheckLength(frame.PayloadData.Length, LimitFor(frame.Opcode));
                    if (!frame.FIN)
                    {
                        _combining = true;
                        _payloadLength = frame.PayloadData.Length;
                    }
                    break;
                case WSDataType.Cont:
                    if (!_combining)
                    {
                        throw new InvalidDataException(
                            "A V2 continuation frame has no initial data frame.");
                    }

                    _payloadLength = checked(_payloadLength + frame.PayloadData.Length);
                    CheckLength(_payloadLength, LimitFor(_initialOpcode));
                    break;
                default:
                    throw new InvalidDataException($"Unsupported V2 WebSocket data opcode '{frame.Opcode}'.");
            }

            var complete = _combinator.TryCombine(frame, out message);
            if (frame.FIN)
            {
                if (!complete)
                    throw new InvalidDataException("TouchSocket did not complete a final V2 text frame.");
                _combining = false;
                _payloadLength = 0;
                _initialOpcode = default;
            }

            if (!complete)
                return false;

            if (message.Opcode is not (WSDataType.Text or WSDataType.Binary))
            {
                throw new InvalidDataException(
                    "The completed V2 WebSocket message is not a supported data message.");
            }

            var opcode = message.Opcode;
            var payload = message.PayloadData.ToArray();
            message.Dispose();
            _combinator.Clear();
            message = new WebSocketMessage(
                opcode,
                new ReadOnlySequence<byte>(payload),
                static () => { });
            return true;
        }
        catch
        {
            Reset();
            message = default;
            throw;
        }
    }

    private void Reset()
    {
        _combinator.Clear();
        _combining = false;
        _payloadLength = 0;
        _initialOpcode = default;
    }

    private static int LimitFor(WSDataType opcode) => opcode == WSDataType.Binary
        ? MaxBinaryMessageSize
        : MaxTextMessageSize;

    private static void CheckLength(int length, int limit)
    {
        if (length > limit)
        {
            throw new InvalidDataException(
                $"A V2 inbound message cannot exceed {limit} bytes.");
        }
    }
}
