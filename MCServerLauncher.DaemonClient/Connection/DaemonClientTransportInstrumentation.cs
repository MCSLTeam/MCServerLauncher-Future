using System;
using System.Diagnostics;
using System.Threading;
using MCServerLauncher.Common.ProtoType.Event;

namespace MCServerLauncher.DaemonClient.Connection;

internal interface IDaemonClientTransportInstrumentation
{
    void OnOutboundSerialize(TimeSpan duration, int payloadBytes);
    void OnInboundActionResponseParse(TimeSpan duration, int payloadBytes);
    void OnInboundEventPacketParse(TimeSpan duration, int payloadBytes);
    void OnEventDataMaterialized(EventType eventType, TimeSpan duration, bool hasPayload);
}

internal sealed class NoopDaemonClientTransportInstrumentation : IDaemonClientTransportInstrumentation
{
    public static readonly NoopDaemonClientTransportInstrumentation Instance = new();

    public void OnOutboundSerialize(TimeSpan duration, int payloadBytes)
    {
    }

    public void OnInboundActionResponseParse(TimeSpan duration, int payloadBytes)
    {
    }

    public void OnInboundEventPacketParse(TimeSpan duration, int payloadBytes)
    {
    }

    public void OnEventDataMaterialized(EventType eventType, TimeSpan duration, bool hasPayload)
    {
    }
}

internal readonly record struct DaemonClientTransportInstrumentationSnapshot(
    long OutboundSerializeSampleCount,
    long OutboundSerializeTotalTicks,
    long OutboundSerializeTotalBytes,
    long InboundActionResponseParseSampleCount,
    long InboundActionResponseParseTotalTicks,
    long InboundActionResponseTotalBytes,
    long InboundEventPacketParseSampleCount,
    long InboundEventPacketParseTotalTicks,
    long InboundEventPacketTotalBytes,
    long EventDataMaterializationSampleCount,
    long EventDataMaterializationTotalTicks,
    long EventDataPayloadCount);

internal sealed class DaemonClientTransportInstrumentationCollector : IDaemonClientTransportInstrumentation
{
    private long _outboundSerializeSampleCount;
    private long _outboundSerializeTotalTicks;
    private long _outboundSerializeTotalBytes;
    private long _inboundActionResponseParseSampleCount;
    private long _inboundActionResponseParseTotalTicks;
    private long _inboundActionResponseTotalBytes;
    private long _inboundEventPacketParseSampleCount;
    private long _inboundEventPacketParseTotalTicks;
    private long _inboundEventPacketTotalBytes;
    private long _eventDataMaterializationSampleCount;
    private long _eventDataMaterializationTotalTicks;
    private long _eventDataPayloadCount;

    public void OnOutboundSerialize(TimeSpan duration, int payloadBytes)
    {
        Interlocked.Increment(ref _outboundSerializeSampleCount);
        Interlocked.Add(ref _outboundSerializeTotalTicks, duration.Ticks);
        Interlocked.Add(ref _outboundSerializeTotalBytes, payloadBytes);
    }

    public void OnInboundActionResponseParse(TimeSpan duration, int payloadBytes)
    {
        Interlocked.Increment(ref _inboundActionResponseParseSampleCount);
        Interlocked.Add(ref _inboundActionResponseParseTotalTicks, duration.Ticks);
        Interlocked.Add(ref _inboundActionResponseTotalBytes, payloadBytes);
    }

    public void OnInboundEventPacketParse(TimeSpan duration, int payloadBytes)
    {
        Interlocked.Increment(ref _inboundEventPacketParseSampleCount);
        Interlocked.Add(ref _inboundEventPacketParseTotalTicks, duration.Ticks);
        Interlocked.Add(ref _inboundEventPacketTotalBytes, payloadBytes);
    }

    public void OnEventDataMaterialized(EventType eventType, TimeSpan duration, bool hasPayload)
    {
        Interlocked.Increment(ref _eventDataMaterializationSampleCount);
        Interlocked.Add(ref _eventDataMaterializationTotalTicks, duration.Ticks);

        if (hasPayload)
            Interlocked.Increment(ref _eventDataPayloadCount);
    }

    public DaemonClientTransportInstrumentationSnapshot Snapshot()
    {
        return new DaemonClientTransportInstrumentationSnapshot(
            Interlocked.Read(ref _outboundSerializeSampleCount),
            Interlocked.Read(ref _outboundSerializeTotalTicks),
            Interlocked.Read(ref _outboundSerializeTotalBytes),
            Interlocked.Read(ref _inboundActionResponseParseSampleCount),
            Interlocked.Read(ref _inboundActionResponseParseTotalTicks),
            Interlocked.Read(ref _inboundActionResponseTotalBytes),
            Interlocked.Read(ref _inboundEventPacketParseSampleCount),
            Interlocked.Read(ref _inboundEventPacketParseTotalTicks),
            Interlocked.Read(ref _inboundEventPacketTotalBytes),
            Interlocked.Read(ref _eventDataMaterializationSampleCount),
            Interlocked.Read(ref _eventDataMaterializationTotalTicks),
            Interlocked.Read(ref _eventDataPayloadCount));
    }
}

internal static class DaemonClientTransportInstrumentationScope
{
    private sealed class ScopeFrame : IDisposable
    {
        public ScopeFrame(IDaemonClientTransportInstrumentation instrumentation, ScopeFrame? parent)
        {
            Instrumentation = instrumentation;
            Parent = parent;
        }

        public IDaemonClientTransportInstrumentation Instrumentation { get; }

        public ScopeFrame? Parent { get; }

        public void Dispose()
        {
            if (ReferenceEquals(_current.Value, this))
                _current.Value = Parent;
        }
    }

    private static readonly AsyncLocal<ScopeFrame?> _current = new();

    public static IDaemonClientTransportInstrumentation Current => _current.Value?.Instrumentation ?? NoopDaemonClientTransportInstrumentation.Instance;

    public static bool TryGetCurrent(out IDaemonClientTransportInstrumentation instrumentation)
    {
        if (_current.Value is null)
        {
            instrumentation = NoopDaemonClientTransportInstrumentation.Instance;
            return false;
        }

        instrumentation = _current.Value.Instrumentation;
        return true;
    }

    public static IDisposable Push(IDaemonClientTransportInstrumentation instrumentation)
    {
        if (instrumentation is null)
            throw new ArgumentNullException(nameof(instrumentation));

        var frame = new ScopeFrame(instrumentation, _current.Value);
        _current.Value = frame;
        return frame;
    }
}

internal static class DaemonClientTransportStopwatch
{
    public static TimeSpan GetElapsedTime(long startTimestamp)
    {
        var elapsedTicks = Stopwatch.GetTimestamp() - startTimestamp;
        var seconds = (double)elapsedTicks / Stopwatch.Frequency;
        return TimeSpan.FromSeconds(seconds);
    }
}
