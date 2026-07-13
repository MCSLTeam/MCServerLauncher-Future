using System;
using System.Buffers;
using System.IO;
using TouchSocket.Http.WebSockets;

namespace MCServerLauncher.DaemonClient.Connection.V2;

internal sealed class TouchSocketV2MessageAssembler
{
    internal const int MaxTextMessageSize = 10 * 1024 * 1024;

    private readonly object _gate = new();
    private readonly WebSocketMessageCombinator _combinator = new();
    private bool _combining;
    private int _payloadLength;

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
                    if (_combining)
                        throw new InvalidDataException("A fragmented V2 text message cannot be interleaved.");
                    CheckLength(frame.PayloadData.Length);
                    if (!frame.FIN)
                    {
                        _combining = true;
                        _payloadLength = frame.PayloadData.Length;
                    }
                    break;
                case WSDataType.Cont:
                    if (!_combining)
                        throw new InvalidDataException("A V2 continuation frame has no initial text frame.");
                    _payloadLength = checked(_payloadLength + frame.PayloadData.Length);
                    CheckLength(_payloadLength);
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
            }

            if (!complete)
                return false;

            if (message.Opcode != WSDataType.Text)
                throw new InvalidDataException("The completed V2 WebSocket message is not text.");

            var payload = message.PayloadData.ToArray();
            message.Dispose();
            _combinator.Clear();
            message = new WebSocketMessage(
                WSDataType.Text,
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
    }

    private static void CheckLength(int length)
    {
        if (length > MaxTextMessageSize)
            throw new InvalidDataException(
                $"A V2 inbound text message cannot exceed {MaxTextMessageSize} bytes.");
    }
}
