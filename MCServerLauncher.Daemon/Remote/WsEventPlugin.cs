using System.Collections.Concurrent;
using System.Threading.Channels;
using MCServerLauncher.Common.ProtoType;
using MCServerLauncher.Common.ProtoType.Event;
using MCServerLauncher.Common.ProtoType.Serialization;
using MCServerLauncher.Daemon.Remote.Event;
using MCServerLauncher.Daemon.Serialization;
using StjJsonSerializer = System.Text.Json.JsonSerializer;
using JsonElement = System.Text.Json.JsonElement;
using TouchSocket.Core;
using TouchSocket.Http;
using TouchSocket.Http.WebSockets;
using TouchSocket.Sockets;

namespace MCServerLauncher.Daemon.Remote;

public class WsEventPlugin : PluginBase, IWsPlugin, IWebSocketClosingPlugin
{
    private readonly IEventService _eventService;
    private readonly Channel<(EventType, IEventMeta?, IEventData?)> _eventChannel;
    private readonly Task _batchProcessorTask;
    private readonly CancellationTokenSource _cts;

    private readonly record struct PreparedEventPayload(JsonPayloadBuffer? EventMeta, JsonPayloadBuffer? EventData);
    private readonly record struct BatchedEvent(EventType Type, IEventMeta? Meta, IEventData? Data);

    public WsEventPlugin(IEventService eventService, WsContextContainer container, IHttpService httpService)
    {
        _eventService = eventService;
        Container = container;
        HttpService = httpService;
        _cts = new CancellationTokenSource();

        _eventChannel = Channel.CreateUnbounded<(EventType, IEventMeta?, IEventData?)>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        _eventService.Signal += (e, m, d) => _eventChannel.Writer.TryWrite((e, m, d));
        _batchProcessorTask = Task.Run(ProcessEventBatchesAsync);
    }

    private async Task ProcessEventBatchesAsync()
    {
        const int batchWindowMs = 10;
        var batch = new List<BatchedEvent>(32);

        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                batch.Clear();

                if (await _eventChannel.Reader.WaitToReadAsync(_cts.Token))
                {
                    var deadline = DateTime.UtcNow.AddMilliseconds(batchWindowMs);

                    while (_eventChannel.Reader.TryRead(out var evt))
                    {
                        batch.Add(new BatchedEvent(evt.Item1, evt.Item2, evt.Item3));

                        if (DateTime.UtcNow >= deadline || batch.Count >= 100)
                            break;
                    }

                    if (batch.Count > 0)
                        await SendBatchedEventsAsync(batch);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
    }

    private async Task SendBatchedEventsAsync(List<BatchedEvent> batch)
    {
        var clientEventMap = new Dictionary<WsContext, List<(EventType, JsonPayloadBuffer?, JsonPayloadBuffer?)>>();

        foreach (var evt in batch)
        {
            if (evt.Meta is not null)
            {
                var preparedPayload = PreparePayload(evt.Meta, evt.Data);

                foreach (var context in EnumerateSubscribedContexts(Container, evt.Type, evt.Meta))
                {
                    if (!clientEventMap.TryGetValue(context, out var events))
                    {
                        events = new List<(EventType, JsonPayloadBuffer?, JsonPayloadBuffer?)>();
                        clientEventMap[context] = events;
                    }
                    events.Add((evt.Type, preparedPayload.EventMeta, preparedPayload.EventData));
                }
            }
            else
            {
                var eventDataBuffer = ToPayloadBuffer(evt.Data);

                foreach (var context in EnumerateSubscribedContexts(Container, evt.Type, evt.Meta))
                {
                    var eventMetas = context.GetEventMetas(evt.Type).ToArray();

                    if (!clientEventMap.TryGetValue(context, out var events))
                    {
                        events = new List<(EventType, JsonPayloadBuffer?, JsonPayloadBuffer?)>();
                        clientEventMap[context] = events;
                    }

                    if (eventMetas.Length == 0)
                    {
                        events.Add((evt.Type, null, eventDataBuffer));
                    }
                    else
                    {
                        foreach (var eventMeta in eventMetas)
                            events.Add((evt.Type, ToPayloadBuffer(eventMeta), eventDataBuffer));
                    }
                }
            }
        }

        var sendTasks = new List<Task>(clientEventMap.Count);
        foreach (var (context, events) in clientEventMap)
        {
            sendTasks.Add(SendEventsToClientAsync(context.GetWebsocket(), events));
        }

        await Task.WhenAll(sendTasks);
    }

    private static async Task SendEventsToClientAsync(IWebSocket ws, List<(EventType Type, JsonPayloadBuffer? Meta, JsonPayloadBuffer? Data)> events)
    {
        foreach (var (type, meta, data) in events)
        {
            await PrivateSendPreparedEvent(type, meta, data, ws);
        }
    }

    public async Task OnWebSocketClosing(IWebSocket webSocket, ClosingEventArgs e)
    {
        this.GetWsContext(webSocket).UnsubscribeAllEvents();
        await e.InvokeNext();
    }

    public IHttpService HttpService { get; init; }
    public WsContextContainer Container { get; init; }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _eventChannel.Writer.Complete();

        try
        {
            await _batchProcessorTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }

        _cts.Dispose();
    }

    private static IEnumerable<WsContext> EnumerateSubscribedContexts(WsContextContainer container, EventType type, IEventMeta? meta)
    {
        foreach (var (_, context) in container)
        {
            if (!context.IsSubscribedEvent(type, meta))
                continue;

            yield return context;
        }
    }

    private static async ValueTask PrivateSendEvent(EventType type, IEventMeta? meta, IEventData? data, IWebSocket ws)
    {
        var preparedPayload = PreparePayload(meta, data);
        await PrivateSendPreparedEvent(type, preparedPayload.EventMeta, preparedPayload.EventData, ws);
    }

    private static async ValueTask PrivateSendPreparedEvent(
        EventType type,
        JsonPayloadBuffer? eventMeta,
        JsonPayloadBuffer? eventData,
        IWebSocket ws)
    {
        await SendTextFrameAsync(ws, BuildWirePayloadUtf8(type, eventMeta, eventData));
    }

    private static PreparedEventPayload PreparePayload(object? meta, object? data)
    {
        return new PreparedEventPayload(ToPayloadBuffer(meta), ToPayloadBuffer(data));
    }

    private static byte[] BuildWirePayloadUtf8(EventType type, JsonPayloadBuffer? eventMeta, JsonPayloadBuffer? eventData)
    {
        var packet = new EventPacket
        {
            EventType = type,
            EventMeta = eventMeta,
            EventData = eventData
        };
        return StjJsonSerializer.SerializeToUtf8Bytes(packet, DaemonRpcTypeInfoCache<EventPacket>.TypeInfo);
    }

    private static async ValueTask SendTextFrameAsync(IWebSocket webSocket, byte[] utf8Payload)
    {
        var frame = new WSDataFrame(utf8Payload)
        {
            Opcode = WSDataType.Text,
            FIN = true
        };
        await webSocket.SendAsync(frame);
    }
    private static JsonPayloadBuffer? ToPayloadBuffer(object? payload)
    {
        if (payload is null)
            return null;

        return payload switch
        {
            JsonPayloadBuffer buffer => buffer,
            JsonElement element => new JsonPayloadBuffer(element.Clone()),
            InstanceLogEventMeta value => new JsonPayloadBuffer(
                StjJsonSerializer.SerializeToElement(value, EventDataContext.Default.InstanceLogEventMeta)),
            InstanceLogEventData value => new JsonPayloadBuffer(
                StjJsonSerializer.SerializeToElement(value, EventDataContext.Default.InstanceLogEventData)),
            DaemonReportEventData value => new JsonPayloadBuffer(
                StjJsonSerializer.SerializeToElement(value, EventDataContext.Default.DaemonReportEventData)),
            _ => throw new NotSupportedException(
                $"WsEventPlugin does not have source-generated JSON metadata for payload type {payload.GetType().FullName}.")
        };
    }
}
