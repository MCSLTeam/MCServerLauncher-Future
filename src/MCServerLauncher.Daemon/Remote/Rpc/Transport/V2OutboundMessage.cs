using System.Collections.Immutable;

namespace MCServerLauncher.Daemon.Remote.Rpc.Transport;

internal enum V2OutboundFrameKind
{
    Text,
    Binary
}

internal readonly record struct V2OutboundFrame
{
    private V2OutboundFrame(V2OutboundFrameKind kind, ImmutableArray<byte> payload)
    {
        if (payload.IsDefault)
            throw new ArgumentException("An outbound frame payload must be initialized.", nameof(payload));

        Kind = kind;
        Payload = payload;
    }

    internal V2OutboundFrameKind Kind { get; }

    internal ImmutableArray<byte> Payload { get; }

    internal static V2OutboundFrame Text(ImmutableArray<byte> payload) =>
        new(V2OutboundFrameKind.Text, payload);

    internal static V2OutboundFrame Binary(ImmutableArray<byte> payload) =>
        new(V2OutboundFrameKind.Binary, payload);

    internal static V2OutboundFrame CopyText(ReadOnlySpan<byte> payload) =>
        Text(ImmutableArray.Create(payload.ToArray()));

    internal static V2OutboundFrame CopyBinary(ReadOnlySpan<byte> payload) =>
        Binary(ImmutableArray.Create(payload.ToArray()));
}

internal sealed class V2OutboundMessage
{
    private V2OutboundMessage(ImmutableArray<V2OutboundFrame> frames)
    {
        Frames = frames;
    }

    internal ImmutableArray<V2OutboundFrame> Frames { get; }

    internal static V2OutboundMessage Single(V2OutboundFrame frame)
    {
        if (frame.Payload.IsDefaultOrEmpty || frame.Kind != V2OutboundFrameKind.Text)
            throw new ArgumentException("A single outbound message must contain one non-empty text frame.", nameof(frame));

        return new V2OutboundMessage([frame]);
    }

    internal static V2OutboundMessage TextThenBinary(
        V2OutboundFrame textFrame,
        V2OutboundFrame binaryFrame)
    {
        if (textFrame.Payload.IsDefaultOrEmpty || textFrame.Kind != V2OutboundFrameKind.Text)
            throw new ArgumentException("The first frame in a text/binary group must be a non-empty text frame.", nameof(textFrame));
        if (binaryFrame.Payload.IsDefaultOrEmpty || binaryFrame.Kind != V2OutboundFrameKind.Binary)
            throw new ArgumentException("The second frame in a text/binary group must be a non-empty binary frame.", nameof(binaryFrame));

        return new V2OutboundMessage([textFrame, binaryFrame]);
    }
}
